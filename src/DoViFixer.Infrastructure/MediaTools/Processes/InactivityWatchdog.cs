using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.MediaTools.Processes;

/// <summary>Cancels <see cref="Token"/> when a job's processes stop making progress.</summary>
/// <remarks>
/// Progress is any I/O byte or a second of CPU time across the whole job, so a tool that works without
/// writing output, or a descendant doing the work, still counts. A hung tool may wake for a few milliseconds
/// at a time; the CPU threshold keeps that from passing as progress.
/// </remarks>
internal sealed class InactivityWatchdog : IDisposable
{
    private static readonly TimeSpan ProgressCpuTime = TimeSpan.FromSeconds(1);
    private readonly CancellationTokenSource stop = new();
    private readonly CancellationTokenSource stalled = new();
    private readonly Task watching;

    public InactivityWatchdog(ProcessJob job, TimeSpan limit, TimeSpan pollInterval, ILogger logger)
    {
        Limit = limit;
        watching = WatchAsync(job, pollInterval, logger);
    }

    public TimeSpan Limit { get; }
    public CancellationToken Token => stalled.Token;
    public bool Stalled => stalled.IsCancellationRequested;

    /// <summary>Stops watching; call before disposing the job.</summary>
    public async Task StopAsync()
    {
        stop.Cancel();
        await watching;
    }

    public void Dispose()
    {
        stop.Cancel();
        stop.Dispose();
        stalled.Dispose();
    }

    private async Task WatchAsync(ProcessJob job, TimeSpan pollInterval, ILogger logger)
    {
        using var timer = new PeriodicTimer(pollInterval);
        var progress = job.ReadActivity();
        long progressAt = Stopwatch.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                var current = job.ReadActivity();
                if (current.TransferredBytes != progress.TransferredBytes || current.CpuTime - progress.CpuTime >= ProgressCpuTime)
                {
                    progress = current;
                    progressAt = Stopwatch.GetTimestamp();
                }
                else if (Stopwatch.GetElapsedTime(progressAt) >= Limit)
                {
                    logger.LogWarning("Native tools made no CPU or I/O progress for {InactivityLimit}; stopping them", Limit);
                    stalled.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Win32Exception ex)
        {
            // Losing the watchdog must not fail a tool that is still working; the time limit still applies.
            logger.LogDebug(ex, "Stopped watching native tools for inactivity");
        }
    }
}
