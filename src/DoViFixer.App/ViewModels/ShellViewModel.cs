using System.Collections.ObjectModel;
using System.ComponentModel;
using DoViFixer.App.Presentation.Application;

namespace DoViFixer.App.ViewModels;
public sealed partial class ShellViewModel : ObservableObject, INavigationGuard, IDisposable
{
    /// <param name="pages">The sidebar pages in display order. They must include the Media page, which the shell wires to Settings.</param>
    /// <param name="settings">Settings, shown in a frame over the pages rather than as a page.</param>
    public ShellViewModel(IEnumerable<INavigationPage> pages, SettingsViewModel settings, StartupDependencyCheckViewModel dependencyCheck, DependencyReportViewModel dependencyReport)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dependencyCheck);
        ArgumentNullException.ThrowIfNull(dependencyReport);
        INavigationPage[] navigationPages = [.. pages];
        var media = navigationPages.OfType<MediaViewModel>().Single();
        this.media = media;
        this.dependencyCheck = dependencyCheck;
        this.dependencyReport = dependencyReport;
        Settings = settings;
        operations = [.. navigationPages.OfType<OperationViewModel>(), settings];
        // Media is shown from the start; InitializeAsync loads its summary as part of startup.
        Navigation = new NavigationService(navigationPages, guard: this, initialPage: media);
        Navigation.PropertyChanged += NavigationChanged;
        Navigation.NavigationFailed += (_, failure) => (CurrentPage as OperationViewModel)?.SetStatus(ViewStatus.Error, failure.Exception.Message);
        // The Media status bar summarizes the output, retention, FEL and archive choices on the Conversion page.
        var conversion = settings.Sections.Pages.OfType<ConversionSettingsSection>().Single();
        nativeTools = settings.Sections.Pages.OfType<NativeToolsSettingsSection>().Single();
        performance = settings.Sections.Pages.OfType<PerformanceSettingsSection>().Single();
        about = settings.Sections.Pages.OfType<AboutSettingsSection>().Single();
        media.RequestOpenSettings += () =>
        {
            if (OpenSettingsCommand.CanExecute(conversion))
            {
                OpenSettingsCommand.Execute(conversion);
            }
        };
        settings.Saved += media.UpdateSettingsSummary;
        settings.LibraryDeleted += media.ClearDeletedLibrary;
        media.ConversionStarting += settings.FlushAsync;
        dependencyReport.PropertyChanged += DependencyReportChanged;
        dependencyCheck.PropertyChanged += DependencyCheckChanged;
        media.ExportResultsCommand.CanExecuteChanged += MediaCommandsChanged;

        foreach (var operation in operations)
        {
            operation.PropertyChanged += OperationChanged;
        }
        RefreshStatusItems();
    }

    private readonly CancellationTokenSource startupCancellation = new();
    private Task? initializationTask;
    private readonly MediaViewModel media;
    private readonly NativeToolsSettingsSection nativeTools;
    private readonly PerformanceSettingsSection performance;
    private readonly AboutSettingsSection about;
    private readonly StartupDependencyCheckViewModel dependencyCheck;
    private readonly DependencyReportViewModel dependencyReport;
    private readonly OperationViewModel[] operations;
    public SettingsViewModel Settings { get; }

    /// <summary>The Media page, whose commands the title bar menu also offers.</summary>
    public MediaViewModel Media => media;

    /// <summary>Page navigation: the sidebar and the retained page host bind to it.</summary>
    public NavigationService Navigation { get; }

    public Task NavigationTask { get; private set; } = Task.CompletedTask;

    public IReadOnlyList<INavigationPage> Pages => Navigation.Pages;

    /// <summary>
    /// Updated in place so unchanged items, such as the startup check's Cancel button, keep their containers.
    /// Status belongs to the shell: the current page's items, then the startup check and dependency warning.
    /// </summary>
    public ObservableCollection<object> StatusItems { get; } = [];

    private void RefreshStatusItems()
    {
        List<object> items = [.. (CurrentPage as OperationViewModel)?.StatusItems ?? []];
        if (dependencyCheck.IsBusy)
        {
            items.Add(dependencyCheck);
        }
        if (dependencyReport.HasWarning)
        {
            items.Add(dependencyReport);
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (i == StatusItems.Count)
            {
                StatusItems.Add(items[i]);
            }
            else if (!Equals(StatusItems[i], items[i]))
            {
                StatusItems[i] = items[i];
            }
        }
        while (StatusItems.Count > items.Count)
        {
            StatusItems.RemoveAt(StatusItems.Count - 1);
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initializationTask is null or { IsFaulted: true })
        {
            initializationTask = InitializeCoreAsync(cancellationToken);
        }
        return initializationTask;
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, startupCancellation.Token);
        using var registration = linked.Token.Register(() => dependencyCheck.CancelCommand.Execute(null));
        try
        {
            await media.RefreshSettingsSummaryAsync(linked.Token);
            await media.LoadLibraryAsync(linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return;
        }

        if (!linked.IsCancellationRequested)
        {
            await dependencyCheck.CheckAsync();
        }
    }

    /// <summary>Navigation waits while an operation runs, settings save, or a save error is unresolved.</summary>
    bool INavigationGuard.CanNavigate => operations.All(o => !o.IsBusy) && !Settings.IsSaving && Settings.SaveError is null;

    /// <summary>Whether the settings frame is shown over the pages. Only its commands change it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseSettingsCommand))]
    public partial bool IsSettingsOpen { get; private set; }

    private bool CanOpenSettings(SettingsSection? section) => CanNavigate && !IsSettingsOpen;

    /// <summary>Opens the settings frame, at <paramref name="section"/> when one is given, and reloads the settings.</summary>
    /// <remarks>Like page navigation, it is unavailable while an operation runs, so options cannot change under a running batch.</remarks>
    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private async Task OpenSettings(SettingsSection? section)
    {
        IsSettingsOpen = true;
        if (section is not null)
        {
            await Settings.Sections.NavigateAsync(section);
        }

        await Settings.LoadCommand.ExecuteAsync(null);
    }

    // A save in progress does not block closing: closing waits for it. An unresolved save error needs the frame to recover.
    private bool CanCloseSettings() => IsSettingsOpen && Settings.IsIdle && Settings.SaveError is null;

    /// <summary>Closes the settings frame once pending changes are saved. A failed save keeps it open, showing the error.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseSettings))]
    private async Task CloseSettings()
    {
        try
        {
            await Settings.FlushAsync();
        }
        catch (InvalidOperationException)
        {
            // The frame shows the save error with its recovery actions.
            return;
        }

        IsSettingsOpen = false;
    }

    partial void OnIsSettingsOpenChanged(bool value) => NotifyMenuCommands();

    // The title bar menu's Media commands show the Media page first, so their results and status are visible.
    private bool CanUseMedia() => !IsSettingsOpen && (ReferenceEquals(CurrentPage, media) || CanNavigate);

    [RelayCommand(CanExecute = nameof(CanAddMedia))]
    private Task AddFiles() => RunOnMediaAsync(media.AddFilesCommand);

    [RelayCommand(CanExecute = nameof(CanAddMedia))]
    private Task AddFolder() => RunOnMediaAsync(media.AddFolderCommand);

    private bool CanAddMedia() => CanUseMedia() && media.AddFilesCommand.CanExecute(null);

    [RelayCommand(CanExecute = nameof(CanExportResults))]
    private Task ExportResults() => RunOnMediaAsync(media.ExportResultsCommand);

    private bool CanExportResults() => CanUseMedia() && media.ExportResultsCommand.CanExecute(null);

    private async Task RunOnMediaAsync(IAsyncRelayCommand command)
    {
        if (!ReferenceEquals(CurrentPage, media))
        {
            NavigationTask = Navigation.NavigateAsync(media);
            await NavigationTask;
        }

        if (ReferenceEquals(CurrentPage, media) && command.CanExecute(null))
        {
            await command.ExecuteAsync(null);
        }
    }

    private bool CanShowPage(INavigationPage? page) => page is not null && !IsSettingsOpen && CanNavigate && !ReferenceEquals(CurrentPage, page);

    /// <summary>Shows a sidebar page, as selecting it in the sidebar does.</summary>
    [RelayCommand(CanExecute = nameof(CanShowPage))]
    private Task ShowPage(INavigationPage? page)
    {
        NavigationTask = Navigation.NavigateAsync(page!);
        return NavigationTask;
    }

    private bool CanOpenSettingsSection() => CanOpenSettings(null);

    // Settings report their results in the settings frame, so the menu opens the section that shows them.
    [RelayCommand(CanExecute = nameof(CanOpenSettingsSection))]
    private Task CheckDependencies() => OpenSettingsAndRunAsync(nativeTools, Settings.CheckCommand);

    [RelayCommand(CanExecute = nameof(CanOpenSettingsSection))]
    private Task CleanLibrary() => OpenSettingsAndRunAsync(performance, Settings.CleanLibraryCommand);

    [RelayCommand(CanExecute = nameof(CanOpenSettingsSection))]
    private Task ExportDiagnostics() => OpenSettingsAndRunAsync(about, Settings.ExportDiagnosticsCommand);

    [RelayCommand(CanExecute = nameof(CanOpenSettingsSection))]
    private Task ShowAbout() => OpenSettings(about);

    private async Task OpenSettingsAndRunAsync(SettingsSection section, IAsyncRelayCommand command)
    {
        await OpenSettings(section);
        if (IsSettingsOpen && command.CanExecute(null))
        {
            await command.ExecuteAsync(null);
        }
    }

    private void MediaCommandsChanged(object? sender, EventArgs e) => ExportResultsCommand.NotifyCanExecuteChanged();

    private void NotifyMenuCommands()
    {
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        ExportResultsCommand.NotifyCanExecuteChanged();
        ShowPageCommand.NotifyCanExecuteChanged();
        CheckDependenciesCommand.NotifyCanExecuteChanged();
        CleanLibraryCommand.NotifyCanExecuteChanged();
        ExportDiagnosticsCommand.NotifyCanExecuteChanged();
        ShowAboutCommand.NotifyCanExecuteChanged();
    }

    public event EventHandler? CanNavigateChanged;

    async Task<bool> INavigationGuard.ConfirmNavigationAsync(INavigationPage from, INavigationPage to, CancellationToken cancellationToken)
    {
        await Settings.FlushAsync(cancellationToken);
        return !operations.Any(o => o.IsBusy);
    }

    public bool CanNavigate => Navigation.CanNavigate;

    public INavigationPage? CurrentPage
    {
        get => Navigation.CurrentPage;
        set
        {
            if (value is null || !CanNavigate || ReferenceEquals(Navigation.CurrentPage, value))
            {
                return;
            }

            NavigationTask = Navigation.NavigateAsync(value);
        }
    }

    private void NavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(INavigationService.CurrentPage))
        {
            RefreshStatusItems();
            NotifyMenuCommands();
        }

        if (e.PropertyName == nameof(INavigationService.CanNavigate))
        {
            OpenSettingsCommand.NotifyCanExecuteChanged();
            NotifyMenuCommands();
        }

        OnPropertyChanged(e.PropertyName);
    }

    private void DependencyReportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DependencyReportViewModel.HasWarning))
        {
            RefreshStatusItems();
        }
    }

    private void DependencyCheckChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationViewModel.IsBusy))
        {
            RefreshStatusItems();
        }
    }

    private void OperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, CurrentPage) && e.PropertyName == nameof(OperationViewModel.StatusItems))
        {
            RefreshStatusItems();
        }

        if (e.PropertyName is nameof(OperationViewModel.IsBusy) or nameof(SettingsViewModel.IsSaving) or nameof(SettingsViewModel.SaveError))
        {
            CanNavigateChanged?.Invoke(this, EventArgs.Empty);
            CloseSettingsCommand.NotifyCanExecuteChanged();
            NotifyMenuCommands();
        }
    }

    /// <summary>Files may have been deleted or moved in Explorer while the user was away from the window.</summary>
    public void WindowActivated() => media.CheckSourceFiles();

    public async Task CancelAndWaitAsync()
    {
        startupCancellation.Cancel();
        dependencyCheck.CancelCommand.Execute(null);
        foreach (var operation in operations)
        {
            operation.CancelCommand.Execute(null);
        }

        await NavigationTask;
        if (initializationTask is { } initialization)
        {
            try
            {
                await initialization;
            }
            catch (Exception) when (initialization.IsFaulted)
            {
                // Initialization failures were already reported to the caller of InitializeAsync.
            }
        }
        await Task.WhenAll(operations.Select(o => o.Completion).Append(dependencyCheck.Completion));
        await media.CancelPreviewsAsync();
        await media.ArchivePairing;
        await media.FlushColumnLayoutAsync();
        await Settings.FlushAsync();
    }

    public void Dispose()
    {
        Settings.LibraryDeleted -= media.ClearDeletedLibrary;
        dependencyReport.PropertyChanged -= DependencyReportChanged;
        dependencyCheck.PropertyChanged -= DependencyCheckChanged;
        media.ExportResultsCommand.CanExecuteChanged -= MediaCommandsChanged;
        Navigation.PropertyChanged -= NavigationChanged;
        Navigation.Dispose();
        startupCancellation.Dispose();
    }
}
