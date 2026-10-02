using System.Collections.ObjectModel;
using System.ComponentModel;
using DoViFixer.App.Presentation.Application;

namespace DoViFixer.App.ViewModels;
public sealed class ShellViewModel : ObservableObject, INavigationGuard, IDisposable
{
    public ShellViewModel(MediaViewModel media, ArchiveViewModel archive, SettingsViewModel settings, StartupDependencyCheckViewModel dependencyCheck, DependencyReportViewModel dependencyReport)
    {
        this.media = media;
        this.dependencyCheck = dependencyCheck;
        this.dependencyReport = dependencyReport;
        Settings = settings;
        operations = [media, archive, settings];
        // Media is shown from the start; InitializeAsync loads its summary as part of startup.
        Navigation = new NavigationService([media, archive, settings], guard: this, initialPage: media);
        Navigation.PropertyChanged += NavigationChanged;
        Navigation.NavigationFailed += (_, failure) => Settings.SetStatus(ViewStatus.Error, failure.Exception.Message);
        media.RequestNavigateToSettings += () => CurrentPage = settings;
        settings.Saved += media.UpdateSettingsSummary;
        settings.CacheClearing += media.CancelPreviewsAsync;
        media.ConversionStarting += settings.FlushAsync;
        dependencyReport.PropertyChanged += DependencyReportChanged;
        dependencyCheck.PropertyChanged += DependencyCheckChanged;

        foreach (var operation in operations)
        {
            operation.PropertyChanged += OperationChanged;
        }
        RefreshStatusItems();
    }

    private readonly CancellationTokenSource startupCancellation = new();
    private Task? initializationTask;
    private readonly MediaViewModel media;
    private readonly StartupDependencyCheckViewModel dependencyCheck;
    private readonly DependencyReportViewModel dependencyReport;
    private readonly OperationViewModel[] operations;
    public SettingsViewModel Settings { get; }

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

    public event EventHandler? CanNavigateChanged;

    async Task<bool> INavigationGuard.ConfirmNavigationAsync(INavigationPage from, INavigationPage to, CancellationToken cancellationToken)
    {
        await Settings.FlushAsync();
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
        await Settings.FlushAsync();
    }

    public void Dispose()
    {
        dependencyReport.PropertyChanged -= DependencyReportChanged;
        dependencyCheck.PropertyChanged -= DependencyCheckChanged;
        Navigation.PropertyChanged -= NavigationChanged;
        Navigation.Dispose();
        startupCancellation.Dispose();
    }
}
