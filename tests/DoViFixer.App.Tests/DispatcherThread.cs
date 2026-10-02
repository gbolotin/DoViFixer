using System.Windows.Threading;

namespace DoViFixer.App.Tests;

/// <summary>
/// Runs a test body on a dedicated STA dispatcher thread, as the WPF app runs its ViewModels.
/// Progress&lt;T&gt; and awaits then marshal back in order instead of racing on the thread pool.
/// </summary>
internal static class DispatcherThread
{
    public static async Task RunAsync(Func<Task> body, TimeSpan timeout)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await body();
                    finished.TrySetResult();
                }
                catch (Exception ex)
                {
                    finished.TrySetException(ex);
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(timeout);
    }
}
