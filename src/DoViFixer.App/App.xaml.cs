using System.Reflection;
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

    /// <summary>The argument setup's uninstaller starts the app with, to remove what the app added for the user.</summary>
    private const string UninstallArgument = "--uninstall";

    /// <summary>The arguments setup starts the app with, to check and remove the settings and library of a previous installation.</summary>
    private const string CheckUserDataArgument = "--check-user-data";
    private const string RemoveUserDataArgument = "--remove-user-data";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains(UninstallArgument, StringComparer.OrdinalIgnoreCase))
        {
            await UninstallAsync();
            Shutdown();
            return;
        }

        if (e.Args.Length > 0 && (e.Args[0].Equals(CheckUserDataArgument, StringComparison.OrdinalIgnoreCase) || e.Args[0].Equals(RemoveUserDataArgument, StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(await UserDataAsync(e.Args));
            return;
        }

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
        // The Store package gives the app its identity, which WpfFoundation.Notifications does not use yet; it shows none.
        if (!AppPackage.IsStoreApp)
        {
            RegisterNotifications(services);
        }

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
            // The Store package declares the File Explorer commands itself.
            var fileExplorerMenu = isAppProcess && !AppPackage.IsStoreApp && settings is not null ? KeepFileExplorerMenuAsync(services!.GetRequiredService<IFileExplorerIntegration>(), shell.Settings, settings.ShowInFileExplorerMenu) : Task.CompletedTask;

            // Only the app looks for updates; it checks now and every few hours while the setting allows it.
            if (isAppProcess)
            {
                shell.Updates.Start();
            }

            await shell.InitializeAsync();
            readyShell = shell;
            await OpenFileExplorerRequestsAsync();
            await fileExplorerMenu;
        }
    }

    /// <summary>
    /// Removes, without a window, what the app added for the user: File Explorer's commands with the Windows 11 package,
    /// and the notification registration with its Start menu shortcut.
    /// </summary>
    private static async Task UninstallAsync()
    {
        var registrations = new ServiceCollection();
        AppComposition.Register(registrations, new ConfigurationBuilder().AddEnvironmentVariables().Build());
        await using var provider = registrations.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<App>>();
        await provider.GetRequiredService<IFileExplorerIntegration>().UpdateAsync(false);
        try
        {
            // Unregister removes what Register added in this process, so it registers first.
            var notifications = provider.GetRequiredService<WindowsNotificationService>();
            notifications.Register(AppId, ApplicationTitle.Name, Path.Combine(AppContext.BaseDirectory, "dovifixer.ico"));
            notifications.Unregister();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove the Windows notification registration");
        }
    }

    /// <summary>
    /// Answers setup without a window. <c>--check-user-data</c> exits with the parts this version can read in bits 0-1 and
    /// those it cannot in bits 2-3, each as 1 for settings and 2 for the library. <c>--remove-user-data settings library</c>
    /// removes the named parts and exits with 0. Any failure exits with -1, so setup keeps what is there.
    /// </summary>
    private static async Task<int> UserDataAsync(string[] args)
    {
        var registrations = new ServiceCollection();
        AppComposition.Register(registrations, new ConfigurationBuilder().AddEnvironmentVariables().Build());
        await using var provider = registrations.BuildServiceProvider();
        var userData = provider.GetRequiredService<IStoredUserData>();
        try
        {
            if (args[0].Equals(CheckUserDataArgument, StringComparison.OrdinalIgnoreCase))
            {
                var state = await userData.InspectAsync(CancellationToken.None);
                return (int)state.Compatible | ((int)state.Incompatible << 2);
            }

            var parts = args.Skip(1).Aggregate(UserDataParts.None, (all, name) => all | Enum.Parse<UserDataParts>(name, ignoreCase: true));
            await userData.RemoveAsync(parts, CancellationToken.None);
            return 0;
        }
        catch (Exception ex)
        {
            provider.GetRequiredService<ILogger<App>>().LogError(ex, "Setup's {Argument} request failed", args[0]);
            return -1;
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
    private static async Task KeepFileExplorerMenuAsync(IFileExplorerIntegration fileExplorer, SettingsViewModel settings, bool show)
    {
        settings.Saved += async saved =>
        {
            if (saved.ShowInFileExplorerMenu != show)
            {
                show = saved.ShowInFileExplorerMenu;
                await fileExplorer.UpdateAsync(show);
            }
        };
        await fileExplorer.UpdateAsync(show);
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
