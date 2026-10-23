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
            if (isAppProcess && settings is not null)
            {
                KeepFileExplorerMenu(services!, shell.Settings, settings.ShowInFileExplorerMenu);
            }

            await shell.InitializeAsync();
            readyShell = shell;
            await OpenFileExplorerRequestsAsync();
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
    private static void KeepFileExplorerMenu(IServiceProvider provider, SettingsViewModel settings, bool show)
    {
        string executable = Path.ChangeExtension(typeof(App).Assembly.Location, ".exe");
        if (!File.Exists(executable))
        {
            return;
        }

        UpdateFileExplorerMenu(provider, show, executable);
        settings.Saved += saved =>
        {
            if (saved.ShowInFileExplorerMenu != show)
            {
                show = saved.ShowInFileExplorerMenu;
                UpdateFileExplorerMenu(provider, show, executable);
            }
        };
    }

    private static void UpdateFileExplorerMenu(IServiceProvider provider, bool show, string executable)
    {
        try
        {
            provider.GetRequiredService<IFileExplorerMenu>().Update(show, executable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // The app works without the menu; files can still be added in the window.
            provider.GetRequiredService<ILogger<App>>().LogWarning(ex, "Could not update the File Explorer menu");
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
