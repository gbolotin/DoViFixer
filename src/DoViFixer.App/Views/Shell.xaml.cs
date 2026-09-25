using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DoViFixer.App.ViewModels;
using Prism.Navigation.Regions;

namespace DoViFixer.App.Views;
public partial class Shell : Window
{
    private readonly ShellViewModel model;
    private readonly IRegionManager regions;
    private bool closing;
    private bool waitingToClose;
    private string activeView = "Media";
    public Shell(ShellViewModel model, IRegionManager regions)
    {
        this.model = model;
        this.regions = regions;
        InitializeComponent();
        DataContext = model;
        model.Media.RequestNavigateToSettings += OnRequestNavigateToSettings;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnRequestNavigateToSettings()
    {
        foreach (ListBoxItem item in Navigation.Items)
        {
            if (Equals(item.Tag, "Settings"))
            {
                Navigation.SelectedItem = item;
                break;
            }
        }
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
        if (!regions.Regions.ContainsRegionWithName("Workspace"))
        {
            return;
        }

        if (sender is not ListBox listBox)
        {
            return;
        }

        string name = (string)((ListBoxItem)listBox.SelectedItem).Tag;
        if (name == activeView)
        {
            return;
        }

        if (!model.CanNavigate)
        {
            listBox.SelectedItem = listBox.Items.Cast<ListBoxItem>().First(item => Equals(item.Tag, activeView));
            return;
        }

        await model.Settings.FlushAsync();
        var region = regions.Regions["Workspace"];
        activeView = name;
        region.Activate(region.GetView(name));
        if (name == "Settings")
        {
            await model.Settings.LoadCommand.ExecuteAsync();
        }
        else if (name == "Media")
        {
            await model.Media.RefreshSettingsSummaryAsync();
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing)
        {
            return;
        }

        e.Cancel = true;
        if (waitingToClose)
        {
            return;
        }

        // Closing a window need not move keyboard focus out of a settings
        // textbox, so commit its LostFocus binding before flushing autosave.
        if (Keyboard.FocusedElement is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        waitingToClose = true;
        IsEnabled = false;
        try
        {
            await model.CancelAndWaitAsync();
            await System.Windows.Threading.Dispatcher.Yield();
            closing = true;
            Close();
        }
        catch (Exception ex)
        {
            model.Settings.Status = ex.Message;
        }
        finally
        {
            waitingToClose = false;
            IsEnabled = true;
        }
    }
}
