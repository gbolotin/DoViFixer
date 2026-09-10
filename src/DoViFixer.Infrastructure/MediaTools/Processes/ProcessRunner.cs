using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.MediaTools.Processes;

internal sealed record ProcessRequest(string Executable, IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null, TimeSpan? Timeout = null, bool AllowWarnings = false, IReadOnlyList<int>? AcceptedExitCodes = null,
    Action<string>? OutputLine = null);
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
        logger.LogDebug("Starting native tool with arguments {Arguments}; working directory {WorkingDirectory}; timeout {Timeout}",
            request.Arguments, request.WorkingDirectory ?? Environment.CurrentDirectory, request.Timeout ?? TimeSpan.FromHours(24));
        try
        {
            var result = await RunCoreAsync(request, cancellationToken);
            logger.LogDebug("Native tool completed with {ExitCode} after {ElapsedMilliseconds} ms", result.ExitCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            // Detection may intentionally try an absent executable; the owning workflow decides whether this is an error.
            logger.LogDebug(ex, "Native tool stopped after {ElapsedMilliseconds} ms; cancellation requested {CancellationRequested}",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, cancellationToken.IsCancellationRequested);
            throw;
        }
    }

    public async Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken)
    {
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
            logger.LogDebug("Starting pipeline {Producer} {ProducerArguments} -> {Consumer} {ConsumerArguments}",
                producer.Executable, producer.Arguments, consumer.Executable, consumer.Arguments);
            targetStarted = target.Start();
            if (!targetStarted) { throw new IOException("Could not start pipeline consumer."); }
            targetError = DrainAsync(target.StandardError);
            targetOutput = DrainAsync(target.StandardOutput, consumer.OutputLine);
            sourceStarted = source.Start();
            if (!sourceStarted) { throw new IOException("Could not start pipeline producer."); }
            source.StandardInput.Close();
            sourceError = DrainAsync(source.StandardError);
            copy = CopyAsync();
            await Task.WhenAll(copy, source.WaitForExitAsync(linked.Token), target.WaitForExitAsync(linked.Token));
            string error = await sourceError + await targetError;
            await targetOutput;
            if (source.ExitCode != 0 || target.ExitCode != 0)
            {
                throw new IOException($"Streaming failed: producer exit {source.ExitCode}, consumer exit {target.ExitCode}: {Tail(error)}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Streaming conversion exceeded its time limit.");
        }
        finally
        {
            linked.Cancel();
            if (sourceStarted && !source.HasExited) { source.Kill(entireProcessTree: true); }
            if (targetStarted && !target.HasExited) { target.Kill(entireProcessTree: true); }
            if (sourceStarted) { await source.WaitForExitAsync(CancellationToken.None); }
            if (targetStarted) { await target.WaitForExitAsync(CancellationToken.None); }
            foreach (Task? task in new Task?[] { copy, sourceError, targetError, targetOutput })
            {
                if (task is not null)
                {
                    try { await task; }
                    catch (Exception ex) { logger.LogDebug(ex, "Pipeline stream ended during cleanup"); }
                }
            }
        }

        async Task CopyAsync()
        {
            try
            {
                await source.StandardOutput.BaseStream.CopyToAsync(target.StandardInput.BaseStream, linked.Token);
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
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory
        };
        foreach (string argument in request.Arguments) { start.ArgumentList.Add(argument); }
        return new Process { StartInfo = start };
    }

    private async Task<ProcessResult> RunCoreAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        using var process = new Process { StartInfo = start };
        using var timeout = new CancellationTokenSource(request.Timeout ?? TimeSpan.FromHours(24));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        if (!process.Start())
        {
            throw new IOException($"Could not start {request.Executable}.");
        }
        process.StandardInput.Close();
        var stdout = DrainAsync(process.StandardOutput, request.OutputLine);
        var stderr = DrainAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            logger.LogDebug("Native tool interrupted; stdout tail {OutputTail}; stderr tail {ErrorTail}", Tail(await stdout), Tail(await stderr));
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{Path.GetFileName(request.Executable)} exceeded its {request.Timeout ?? TimeSpan.FromHours(24)} time limit.");
        }
        var result = new ProcessResult(process.ExitCode, await stdout, await stderr);
        logger.LogDebug("Native tool exit {ExitCode}; stdout characters {OutputLength}, tail {OutputTail}; stderr characters {ErrorLength}, tail {ErrorTail}",
            result.ExitCode, result.Output.Length, Tail(result.Output), result.Error.Length, Tail(result.Error));
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
