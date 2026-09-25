using System.ComponentModel;
using DoViFixer.App.Presentation;

namespace DoViFixer.App.ViewModels;
public sealed class ShellViewModel : ObservableObject
{
    public ShellViewModel(MediaViewModel media, ArchiveViewModel archive, SettingsViewModel settings)
    {
        Media = media;
        Archive = archive;
        Settings = settings;
        Settings.Saved += Media.UpdateSettingsSummary;
        Media.ConversionStarting += Settings.FlushAsync;
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
    public bool CanNavigate => Operations.All(o => !o.IsBusy) && !Settings.IsSaving && Settings.SaveError is null;

    private void OperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OperationViewModel.IsBusy) or nameof(SettingsViewModel.IsSaving) or nameof(SettingsViewModel.SaveError))
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
        await Settings.FlushAsync();
    }
}
