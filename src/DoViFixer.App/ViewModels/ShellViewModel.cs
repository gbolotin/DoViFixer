using System.ComponentModel;
using DoViFixer.App.Presentation;

namespace DoViFixer.App.ViewModels;
public sealed class ShellViewModel : BindableBase
{
    public ShellViewModel(MediaViewModel media, ArchiveViewModel archive, SettingsViewModel settings)
    {
        Media = media;
        Archive = archive;
        Settings = settings;
        foreach (var operation in Operations)
        {
            operation.PropertyChanged += OperationChanged;
        }
    }

    public MediaViewModel Media
    {
        get;
    }
    public ArchiveViewModel Archive
    {
        get;
    }
    public SettingsViewModel Settings
    {
        get;
    }
    public OperationViewModel[] Operations => [Media, Archive, Settings];
    public bool CanNavigate => Operations.All(o => !o.IsBusy);

    private void OperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationViewModel.IsBusy))
        {
            RaisePropertyChanged(nameof(CanNavigate));
        }
    }

    public async Task CancelAndWaitAsync()
    {
        foreach (var operation in Operations)
        {
            operation.CancelCommand.Execute();
        }

        await Task.WhenAll(Operations.Select(o => o.Completion));
    }
}
