using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DoViFixer.App.ViewModels;

namespace DoViFixer.App.Views;
public partial class Shell : Window
{
    private readonly ShellViewModel model;
    private readonly Dictionary<string, UserControl> views = new();
    private bool closing;
    private bool waitingToClose;
    private string activeView = "Media";
    public Shell(ShellViewModel model)
    {
        this.model = model;
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
        views.Add("Media", new MediaView(model.Media));
        views.Add("Archive", new ArchiveView(model.Archive));
        views.Add("Settings", new SettingsView(model.Settings));
        Workspace.Content = views["Media"];
    }

    private async void Navigate(object sender, RoutedEventArgs e)
    {
        if (views.Count == 0)
        {
            return;
        }

        if (sender is not ListBox listBox || !ReferenceEquals(e.OriginalSource, listBox) || listBox.SelectedItem is not ListBoxItem selected)
        {
            return;
        }

        string name = (string)selected.Tag;
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
        activeView = name;
        Workspace.Content = views[name];
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
