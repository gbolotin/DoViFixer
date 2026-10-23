using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace DoViFixer.App.Startup;

/// <summary>
/// Keeps one window per user session. The first process owns a named mutex and listens on a named pipe; a later start,
/// such as a File Explorer command for each selected file, sends its command line there and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex? mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource stopping = new();
    private Task listening = Task.CompletedTask;

    private SingleInstance(Mutex? mutex, string pipeName)
    {
        this.mutex = mutex;
        this.pipeName = pipeName;
    }

    /// <summary>
    /// A name for this user's instance in this Windows session, which <paramref name="appId"/> identifies. Pipe names
    /// are shared by all sessions on the computer, so the name includes both.
    /// </summary>
    public static string NameFor(string appId)
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return $"{appId}.{identity.User?.Value ?? Environment.UserName}.{process.SessionId}";
    }

    /// <summary>
    /// Returns the instance when this process is the first, or <see langword="null"/> after it sent
    /// <paramref name="arguments"/> to the first one. A first instance that does not answer in
    /// <paramref name="timeout"/> leaves this process to run on its own, so a start never does nothing.
    /// </summary>
    public static async Task<SingleInstance?> StartAsync(string name, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        Mutex? mutex;
        bool owned;
        try
        {
            mutex = new Mutex(false, $@"Local\{name}");
        }
        catch (UnauthorizedAccessException)
        {
            // An instance that runs as administrator owns it.
            mutex = null;
        }

        try
        {
            owned = mutex?.WaitOne(0) == true;
        }
        catch (AbandonedMutexException)
        {
            // The first instance ended without releasing it; this process owns it now.
            owned = true;
        }

        if (!owned && await TrySendAsync(name, arguments, timeout))
        {
            mutex?.Dispose();
            return null;
        }

        return new SingleInstance(mutex, name);
    }

    /// <summary>Calls <paramref name="received"/>, on a worker thread, with the command line of each later start.</summary>
    public void Listen(Action<IReadOnlyList<string>> received)
    {
        ArgumentNullException.ThrowIfNull(received);
        listening = ListenAsync(received, stopping.Token);
    }

    private async Task ListenAsync(Action<IReadOnlyList<string>> received, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(token);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                return;
            }
            catch (IOException)
            {
                // A client that left before connecting; wait for the next one.
                await server.DisposeAsync();
                continue;
            }

            // Wait for the next start while this one is read, so many starts at once do not queue behind each other.
            _ = ReceiveAsync(server, received, token);
        }
    }

    private static async Task ReceiveAsync(NamedPipeServerStream server, Action<IReadOnlyList<string>> received, CancellationToken token)
    {
        await using (server)
        {
            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8);
                List<string> arguments = [];
                while (await reader.ReadLineAsync(token) is { } line)
                {
                    arguments.Add(line);
                }

                received(arguments);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                // A start that broke off sends nothing.
            }
        }
    }

    private static async Task<bool> TrySendAsync(string name, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync((int)timeout.TotalMilliseconds);
            // Windows lets only the foreground process bring a window forward. File Explorer started this one in the
            // foreground, so it lets the first instance come forward to show the files.
            if (GetNamedPipeServerProcessId(client.SafePipeHandle, out uint processId))
            {
                AllowSetForegroundWindow(processId);
            }

            await using var writer = new StreamWriter(client, new UTF8Encoding(false));
            // A path cannot contain a line break, so each argument is one line.
            foreach (string argument in arguments)
            {
                await writer.WriteLineAsync(argument);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        stopping.Cancel();
        try
        {
            listening.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // The listener only ends; its errors no longer matter.
        }

        try
        {
            mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned, or owned by another thread; Windows releases it when the process ends.
        }

        mutex?.Dispose();
        stopping.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
