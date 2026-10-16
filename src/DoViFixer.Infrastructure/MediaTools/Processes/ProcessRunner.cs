using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.MediaTools.Processes;
// OutputRelay applies to a PipeAsync producer: it copies the producer's output to the consumer and may transform it.
internal sealed record ProcessRequest(string Executable, IReadOnlyList<string> Arguments, string? WorkingDirectory = null, TimeSpan? Timeout = null, bool AllowWarnings = false, IReadOnlyList<int>? AcceptedExitCodes = null, Action<string>? OutputLine = null, Func<Stream, Stream, CancellationToken, Task>? OutputRelay = null);
internal sealed record ProcessResult(int ExitCode, string Output, string Error);
internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
    Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken);
}

internal sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ToolInvocationId"] = Guid.NewGuid(), ["Tool"] = Path.GetFileName(request.Executable), ["ToolPath"] = request.Executable
        });
        long started = Stopwatch.GetTimestamp();
        logger.LogDebug("Starting native tool with arguments {Arguments}; working directory {WorkingDirectory}; timeout {Timeout}", request.Arguments, request.WorkingDirectory ?? Environment.CurrentDirectory, request.Timeout ?? TimeSpan.FromHours(24));
        try
        {
            var result = await RunCoreAsync(request, cancellationToken);
            logger.LogDebug("Native tool completed with {ExitCode} after {ElapsedMilliseconds} ms", result.ExitCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            // Detection may intentionally try an absent executable; the owning workflow decides whether this is an error.
            logger.LogDebug(ex, "Native tool stopped after {ElapsedMilliseconds} ms; cancellation requested {CancellationRequested}", Stopwatch.GetElapsedTime(started).TotalMilliseconds, cancellationToken.IsCancellationRequested);
            throw;
        }
    }

    public async Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var job = new ProcessJob();
        using var timeout = new CancellationTokenSource(TimeSpan.FromHours(24));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var source = CreatePipelineProcess(producer);
        using var target = CreatePipelineProcess(consumer);
        bool sourceStarted = false, targetStarted = false;
        Task<string>? sourceError = null, targetError = null, targetOutput = null;
        Task? copy = null;
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            logger.LogDebug("Starting pipeline {Producer} {ProducerArguments} -> {Consumer} {ConsumerArguments}", producer.Executable, producer.Arguments, consumer.Executable, consumer.Arguments);
            await job.StartAsync(target);
            targetStarted = true;
            logger.LogDebug("Started protected pipeline consumer {NativeProcessId}", target.Id);

            targetError = DrainAsync(target.StandardError);
            targetOutput = DrainAsync(target.StandardOutput, consumer.OutputLine);
            await job.StartAsync(source);
            sourceStarted = true;
            logger.LogDebug("Started protected pipeline producer {NativeProcessId}", source.Id);

            source.StandardInput.Close();
            sourceError = DrainAsync(source.StandardError);
            copy = CopyAsync();
            await Task.WhenAll(copy, source.WaitForExitAsync(linked.Token), target.WaitForExitAsync(linked.Token));
            // Descendants may retain redirected handles after the direct processes exit.
            job.Dispose();
            string error = await sourceError + await targetError;
            await targetOutput;
            if (source.ExitCode != 0 || target.ExitCode != 0)
            {
                throw new IOException($"Streaming failed: producer exit {source.ExitCode}, consumer exit {target.ExitCode}: {Tail(error)}");
            }
        }
        catch (OperationCanceledException)when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Streaming conversion exceeded its time limit.");
        }
        finally
        {
            linked.Cancel();
            job.Dispose();

            if (sourceStarted)
            {
                await ProcessJob.StopAsync(source);
            }

            if (targetStarted)
            {
                await ProcessJob.StopAsync(target);
            }

            foreach (Task? task in new Task? []
            {
                copy,
                sourceError,
                targetError,
                targetOutput
            }

            )
            {
                if (task is not null)
                {
                    try
                    {
                        await task;
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Pipeline stream ended during cleanup");
                    }
                }
            }
        }

        async Task CopyAsync()
        {
            try
            {
                var relay = producer.OutputRelay ?? ((input, output, token) => input.CopyToAsync(output, token));
                await relay(source.StandardOutput.BaseStream, target.StandardInput.BaseStream, linked.Token);
            }
            catch
            {
                linked.Cancel();
                throw;
            }
            finally
            {
                target.StandardInput.Close();
            }
        }
    }

    private static Process CreatePipelineProcess(ProcessRequest request)
    {
        var start = new ProcessStartInfo(request.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory
        };
        foreach (string argument in request.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return new Process
        {
            StartInfo = start
        };
    }

    private async Task<ProcessResult> RunCoreAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var job = new ProcessJob();
        var start = new ProcessStartInfo(request.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory
        };
        foreach (string argument in request.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = start
        };
        using var timeout = new CancellationTokenSource(request.Timeout ?? TimeSpan.FromHours(24));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        await job.StartAsync(process);
        Task<string>? stdout = null, stderr = null;
        try
        {
            logger.LogDebug("Started protected native process {NativeProcessId}", process.Id);
            process.StandardInput.Close();
            stdout = DrainAsync(process.StandardOutput, request.OutputLine);
            stderr = DrainAsync(process.StandardError);
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"{Path.GetFileName(request.Executable)} exceeded its {request.Timeout ?? TimeSpan.FromHours(24)} time limit.");
        }
        finally
        {
            // Close the job before draining: descendants can otherwise keep stdout/stderr open forever.
            job.Dispose();
            await ProcessJob.StopAsync(process);
            foreach (var drain in new[] { stdout, stderr })
            {
                if (drain is not null)
                {
                    try
                    {
                        string output = await drain;
                        if (linked.IsCancellationRequested)
                        {
                            logger.LogDebug("Native tool interrupted; output tail {OutputTail}", Tail(output));
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Native tool stream ended during cleanup");
                    }
                }
            }
        }

        var result = new ProcessResult(process.ExitCode, await stdout, await stderr);
        logger.LogDebug("Native tool exit {ExitCode}; stdout characters {OutputLength}, tail {OutputTail}; stderr characters {ErrorLength}, tail {ErrorTail}", result.ExitCode, result.Output.Length, Tail(result.Output), result.Error.Length, Tail(result.Error));
        if (result.ExitCode != 0 && !(request.AllowWarnings && result.ExitCode == 1) && !(request.AcceptedExitCodes?.Contains(result.ExitCode) ?? false))
        {
            throw new IOException($"{Path.GetFileName(request.Executable)} exited {result.ExitCode}: {Tail(result.Error + result.Output)}");
        }

        if (result.ExitCode == 1 && request.AllowWarnings)
        {
            logger.LogWarning("{Tool}: {Warning}", Path.GetFileName(request.Executable), Tail(result.Error + result.Output));
        }

        return result;
    }

    internal static async Task<string> DrainAsync(StreamReader reader, Action<string>? outputLine = null)
    {
        const int limit = 16 * 1024 * 1024;
        var builder = new StringBuilder();
        var buffer = new char[8192];
        bool truncated = false;
        var line = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            int retained = Math.Min(read, limit - builder.Length);
            builder.Append(buffer, 0, retained);
            truncated |= retained < read;
            if (outputLine is not null)
            {
                for (int i = 0; i < read; i++)
                {
                    char character = buffer[i];
                    if (character is '\r' or '\n')
                    {
                        if (line.Length > 0)
                        {
                            outputLine(line.ToString());
                            line.Clear();
                        }
                    }
                    else if (line.Length < limit)
                    {
                        line.Append(character);
                    }
                }
            }
        }

        if (line.Length > 0)
        {
            outputLine?.Invoke(line.ToString());
        }

        if (truncated)
        {
            throw new InvalidDataException("Native tool output exceeded 16 MiB; use file-based output for large data.");
        }

        return builder.ToString();
    }

    private static string Tail(string text) => text.Length > 3000 ? text[^3000..] : text.Trim();
}
