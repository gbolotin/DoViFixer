using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DoViFixer.App.ViewModels;
using DoViFixer.App.Navigation;

namespace DoViFixer.App.Views;
public partial class Shell : Window
{
    private readonly ShellViewModel model;
    private bool closing;
    private bool waitingToClose;
    public Shell(ShellViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        Closing += OnClosing;
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
