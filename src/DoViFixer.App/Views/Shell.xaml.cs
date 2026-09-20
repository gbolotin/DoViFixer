using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DoViFixer.App.ViewModels;
using Prism.Navigation.Regions;

namespace DoViFixer.App.Views;
public partial class Shell : Window
{
    private readonly ShellViewModel model;
    private readonly IRegionManager regions;
    private bool closing;
    public Shell(ShellViewModel model, IRegionManager regions)
    {
        this.model = model;
        this.regions = regions;
        InitializeComponent();
        DataContext = model;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        var region = regions.Regions["Workspace"];
        region.Add(new MediaView(model.Media), "Media");
        region.Add(new ArchiveView(model.Archive), "Archive");
        region.Add(new SettingsView(model.Settings), "Settings");
        region.Activate(region.GetView("Media"));
    }

    private async void Navigate(object sender, RoutedEventArgs e)
    {
        if (!model.CanNavigate)
        {
            return;
        }

        string name = (string)((Button)sender).Tag;
        var region = regions.Regions["Workspace"];
        region.Activate(region.GetView(name));
        foreach (Button button in Navigation.Children)
        {
            if ((string)button.Tag == name)
            {
                button.SetResourceReference(BorderBrushProperty, SystemColors.AccentColorBrushKey);
            }
            else
            {
                button.ClearValue(BorderBrushProperty);
            }
        }

        if (name == "Settings")
        {
            await model.Settings.LoadCommand.ExecuteAsync();
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing || model.CanNavigate)
        {
            return;
        }

        e.Cancel = true;
        await model.CancelAndWaitAsync();
        closing = true;
        Close();
    }
}
