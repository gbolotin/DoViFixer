using System.ComponentModel;
using DoViFixer.App.Navigation;
using DoViFixer.App.Presentation;

namespace DoViFixer.App.ViewModels;
public sealed class ShellViewModel : ObservableObject, IInitializeAsync
{
    public ShellViewModel(MediaViewModel media, ArchiveViewModel archive, SettingsViewModel settings)
    {
        this.media = media;
        Settings = settings;
        Pages = [media, archive, settings];
        operations = [.. Pages.OfType<OperationViewModel>()];
        media.RequestNavigateToSettings += () => CurrentPage = settings;
        settings.Saved += media.UpdateSettingsSummary;
        media.ConversionStarting += settings.FlushAsync;

        currentPage = media;
        foreach (var operation in operations)
        {
            operation.PropertyChanged += OperationChanged;
        }
    }

    private bool isInitialized;
    private readonly MediaViewModel media;
    private readonly OperationViewModel[] operations;
    private INavigationPage currentPage;
    private bool navigating;
    public SettingsViewModel Settings { get; }
    public Task NavigationTask { get; private set; } = Task.CompletedTask;

    public INavigationPage[] Pages { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (isInitialized)
        {
            return;
        }

        foreach (var page in Pages.OfType<IInitializeAsync>())
        {
            await page.InitializeAsync(cancellationToken);
        }

        isInitialized = true;
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
            Settings.Status = ex.Message;
        }
        finally
        {
            navigating = false;
            RaisePropertyChanged(nameof(CanNavigate));
        }
    }

    private void OperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OperationViewModel.IsBusy) or nameof(SettingsViewModel.IsSaving) or nameof(SettingsViewModel.SaveError))
        {
            RaisePropertyChanged(nameof(CanNavigate));
        }
    }

    public async Task CancelAndWaitAsync()
    {
        foreach (var operation in operations)
        {
            operation.CancelCommand.Execute();
        }

        await NavigationTask;
        await Task.WhenAll(operations.Select(o => o.Completion));
        await Settings.FlushAsync();
    }
}
