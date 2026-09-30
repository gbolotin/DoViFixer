using System.Collections.ObjectModel;
using System.ComponentModel;
using DoViFixer.App.Navigation;
using DoViFixer.App.Presentation.Common;
using DoViFixer.App.Presentation.Application;

namespace DoViFixer.App.ViewModels;
public sealed class ShellViewModel : ObservableObject, IInitializeAsync, IDisposable
{
    public ShellViewModel(MediaViewModel media, ArchiveViewModel archive, SettingsViewModel settings, StartupDependencyCheckViewModel dependencyCheck, DependencyReportViewModel dependencyReport)
    {
        this.media = media;
        this.dependencyCheck = dependencyCheck;
        this.dependencyReport = dependencyReport;
        Settings = settings;
        Pages = [media, archive, settings];
        operations = [.. Pages.OfType<OperationViewModel>()];
        media.RequestNavigateToSettings += () => CurrentPage = settings;
        settings.Saved += media.UpdateSettingsSummary;
        settings.CacheClearing += media.CancelPreviewsAsync;
        media.ConversionStarting += settings.FlushAsync;
        dependencyReport.PropertyChanged += DependencyReportChanged;
        dependencyCheck.PropertyChanged += DependencyCheckChanged;

        currentPage = media;
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
    private INavigationPage currentPage;
    private bool navigating;
    public SettingsViewModel Settings { get; }
    public Task NavigationTask { get; private set; } = Task.CompletedTask;

    public INavigationPage[] Pages { get; }

    /// <summary>
    /// Updated in place so unchanged items, such as the startup check's Cancel button, keep their containers.
    /// </summary>
    public ObservableCollection<object> StatusItems { get; } = [];

    private void RefreshStatusItems()
    {
        List<object> items = [.. CurrentPage.StatusItems];
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
        using var registration = linked.Token.Register(() => dependencyCheck.CancelCommand.Execute());
        try
        {
            foreach (var page in Pages.OfType<IInitializeAsync>())
            {
                await page.InitializeAsync(linked.Token);
            }
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

    public bool CanNavigate => !navigating && operations.All(o => !o.IsBusy) && !Settings.IsSaving && Settings.SaveError is null;

    public INavigationPage CurrentPage
    {
        get => currentPage;
        set
        {
            if (value is null || !CanNavigate || ReferenceEquals(currentPage, value))
            {
                return;
            }

            NavigationTask = NavigateAsync(value);
        }
    }

    private async Task NavigateAsync(INavigationPage page)
    {
        navigating = true;
        RaisePropertyChanged(nameof(CanNavigate));

        try
        {
            await Settings.FlushAsync();
            if (operations.Any(o => o.IsBusy))
            {
                return;
            }

            SetProperty(ref currentPage, page, nameof(CurrentPage));
            RefreshStatusItems();
            if (ReferenceEquals(page, Settings))
            {
                await Settings.LoadCommand.ExecuteAsync();
            }
            else if (ReferenceEquals(page, media))
            {
                await media.RefreshSettingsSummaryAsync();
            }
        }
        catch (Exception ex)
        {
            Settings.SetStatus(ViewStatus.Error, ex.Message);
        }
        finally
        {
            navigating = false;
            RaisePropertyChanged(nameof(CanNavigate));
        }
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
            RaisePropertyChanged(nameof(CanNavigate));
        }
    }

    public async Task CancelAndWaitAsync()
    {
        startupCancellation.Cancel();
        dependencyCheck.CancelCommand.Execute();
        foreach (var operation in operations)
        {
            operation.CancelCommand.Execute();
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
        startupCancellation.Dispose();
    }
}
