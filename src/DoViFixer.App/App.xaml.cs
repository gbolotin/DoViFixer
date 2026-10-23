using System.Reflection;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using DoViFixer.App.Composition;
using DoViFixer.App.Presentation.Application;
using DoViFixer.App.Startup;
using DoViFixer.App.ViewModels;
using DoViFixer.App.Views;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.FileExplorer;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Updates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WpfFoundation.Notifications;

namespace DoViFixer.App;
public partial class App : System.Windows.Application
{
    private const string AppId = "gbolotin.DoViFixer";
    private ServiceProvider? services;
    private SingleInstance? instance;
    // Requests from File Explorer that wait for the window to finish starting or for the batch to gather; UI thread only.
    private readonly List<FileExplorerRequest> fileExplorerRequests = [];
    private DispatcherTimer? fileExplorerBatch;
    private ShellViewModel? readyShell;
    // A change of the setting waits for the update at startup, so two never change the menu at once.
    private static readonly SemaphoreSlim fileExplorerMenuUpdate = new(1, 1);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var request = FileExplorerRequest.Parse(e.Args, Environment.CurrentDirectory);
        // Only DoViFixer.App.exe keeps one window and offers File Explorer's menu, not a test host that runs this class.
        bool isAppProcess = Assembly.GetEntryAssembly() == typeof(App).Assembly;
        if (isAppProcess)
        {
            instance = await SingleInstance.StartAsync(SingleInstance.NameFor(AppId), request.ToArguments(), TimeSpan.FromSeconds(10));
            if (instance is null)
            {
                // The window that is already open shows the files.
                Shutdown();
                return;
            }

            instance.Listen(arguments => Dispatcher.BeginInvoke(() => ReceiveFromFileExplorer(FileExplorerRequest.Parse(arguments, Environment.CurrentDirectory), bringToFront: true)));
        }

        var registrations = new ServiceCollection();
        AppComposition.Register(registrations, new ConfigurationBuilder().AddEnvironmentVariables().Build());
        services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        RegisterNotifications(services);
        MainWindow = services.GetRequiredService<Shell>();
        var settingsService = services.GetRequiredService<SettingsService>();
        var themeService = services.GetRequiredService<IThemeService>();
        MainWindow.Show();
        ReceiveFromFileExplorer(request, bringToFront: false);
        UserSettings? settings = null;
        try
        {
            settings = await settingsService.ReadAsync(CancellationToken.None);
            if (services is not null)
            {
                themeService.ApplyTheme(settings.Theme.ToThemePreference());
            }
        }
        catch
        {
            // If settings cannot be loaded, keep the default theme.
        }

        if (MainWindow.DataContext is ShellViewModel shell)
        {
            // Registering the package can take a few seconds; the window does not wait for it.
            var fileExplorerMenu = isAppProcess && settings is not null ? KeepFileExplorerMenuAsync(services!, shell.Settings, settings.ShowInFileExplorerMenu) : Task.CompletedTask;

            await shell.InitializeAsync();
            readyShell = shell;
            await OpenFileExplorerRequestsAsync();
            await fileExplorerMenu;
        }
    }

    // At every start and before the main window exists, so Windows shows the app's name and icon on its notifications,
    // routes clicks back to this window, and groups the window with the Start menu shortcut it creates.
    private static void RegisterNotifications(IServiceProvider provider)
    {
        try
        {
            provider.GetRequiredService<WindowsNotificationService>().Register(AppId, ApplicationTitle.Name, Path.Combine(AppContext.BaseDirectory, "dovifixer.ico"));
        }
        catch (Exception ex)
        {
            // The app works without notifications; the taskbar still reports operations.
            provider.GetRequiredService<ILogger<App>>().LogWarning(ex, "Could not register for Windows notifications");
        }
    }

    /// <summary>
    /// Updates File Explorer's menu at every start, so its commands follow the app when its folder moves, and whenever
    /// the setting changes.
    /// </summary>
    private static async Task KeepFileExplorerMenuAsync(IServiceProvider provider, SettingsViewModel settings, bool show)
    {
        string executable = Path.ChangeExtension(typeof(App).Assembly.Location, ".exe");
        if (!File.Exists(executable))
        {
            return;
        }

        settings.Saved += async saved =>
        {
            if (saved.ShowInFileExplorerMenu != show)
            {
                show = saved.ShowInFileExplorerMenu;
                await UpdateFileExplorerMenuAsync(provider, show, executable);
            }
        };
        await UpdateFileExplorerMenuAsync(provider, show, executable);
    }

    /// <summary>
    /// Shows the commands in Windows 11's menu through the app's package, or, before Windows 11 or when Windows does not
    /// register the package, as classic commands under "Show more options".
    /// </summary>
    private static async Task UpdateFileExplorerMenuAsync(IServiceProvider provider, bool show, string executable)
    {
        var logger = provider.GetRequiredService<ILogger<App>>();
        await fileExplorerMenuUpdate.WaitAsync();
        try
        {
            bool packaged = false;
            try
            {
                packaged = await FileExplorerPackage.UpdateAsync(show, Path.GetDirectoryName(executable)!);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not update the Windows 11 File Explorer menu package; using the classic File Explorer menu");
            }

            try
            {
                provider.GetRequiredService<IFileExplorerMenu>().Update(show && !packaged, executable);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // The app works without the menu; files can still be added in the window.
                logger.LogWarning(ex, "Could not update the File Explorer menu");
            }
        }
        finally
        {
            fileExplorerMenuUpdate.Release();
        }
    }

    private void ReceiveFromFileExplorer(FileExplorerRequest request, bool bringToFront)
    {
        if (bringToFront && MainWindow is { } window)
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
        }

        if (request.Paths.Count == 0)
        {
            return;
        }

        fileExplorerRequests.Add(request);
        // File Explorer starts the app once for each selected file within moments; a short pause gathers them into one batch.
        fileExplorerBatch ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, async (_, _) => await OpenFileExplorerRequestsAsync(), Dispatcher);
        fileExplorerBatch.Stop();
        fileExplorerBatch.Start();
    }

    private async Task OpenFileExplorerRequestsAsync()
    {
        fileExplorerBatch?.Stop();
        if (readyShell is null || fileExplorerRequests.Count == 0)
        {
            return;
        }

        FileExplorerRequest[] requests = [.. fileExplorerRequests];
        fileExplorerRequests.Clear();
        foreach (var batch in requests.GroupBy(request => request.Convert))
        {
            await readyShell.OpenFromFileExplorerAsync(new([.. batch.SelectMany(request => request.Paths)], batch.Key));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        fileExplorerBatch?.Stop();
        instance?.Dispose();
        instance = null;
        services?.Dispose();
        services = null;
        base.OnExit(e);
    }
}
