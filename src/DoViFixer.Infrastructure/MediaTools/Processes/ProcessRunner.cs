using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.MediaTools.Processes;

internal sealed record ProcessRequest(string Executable, IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null, TimeSpan? Timeout = null, bool AllowWarnings = false, IReadOnlyList<int>? AcceptedExitCodes = null);
internal sealed record ProcessResult(int ExitCode, string Output, string Error);
internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}
internal sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
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
        var stdout = DrainAsync(process.StandardOutput);
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
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{Path.GetFileName(request.Executable)} exceeded its {request.Timeout ?? TimeSpan.FromHours(24)} time limit.");
        }
        var result = new ProcessResult(process.ExitCode, await stdout, await stderr);
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

    private static async Task<string> DrainAsync(StreamReader reader)
    {
        const int limit = 16 * 1024 * 1024;
        var builder = new StringBuilder();
        var buffer = new char[8192];
        bool truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            int retained = Math.Min(read, limit - builder.Length);
            builder.Append(buffer, 0, retained);
            truncated |= retained < read;
        }
        if (truncated)
        {
            throw new InvalidDataException("Native tool output exceeded 16 MiB; use file-based output for large data.");
        }
        return builder.ToString();
    }
    private static string Tail(string text) => text.Length > 3000 ? text[^3000..] : text.Trim();
}
