using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Diagnostics;

namespace DoViFixer.App.Dialogs;
public sealed class UserDialogs(string logDirectory) : IUserDialogs
{
    public void OpenLogs() => OpenFolder(logDirectory);
    public void OpenFolder(string path)
    {
        try
        {
            var start = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = false
            };
            start.ArgumentList.Add(Path.GetFullPath(path));
            using var process = Process.Start(start);
        }
        catch (Exception ex)when (ex is IOException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            MessageBox.Show(ex.Message, "Unable to open folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public string[] PickFiles(string filter = "Matroska media|*.mkv")
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Multiselect = true,
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog();
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public bool Review(string title, string content, string approveLabel, string? requiredText = null)
    {
        var window = new Window
        {
            Title = title,
            Width = 850,
            Height = 600,
            MinWidth = 600,
            MinHeight = 400,
            Owner = System.Windows.Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var root = new DockPanel
        {
            Margin = new Thickness(24)
        };
        var footer = new StackPanel
        {
            Margin = new Thickness(0, 16, 0, 0)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        var input = new TextBox();
        if (requiredText is not null)
        {
            footer.Children.Add(new TextBlock
            {
                Text = "Type " + requiredText + " to confirm deletion.", Margin = new Thickness(0, 0, 0, 8)
            });
            footer.Children.Add(input);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true
        };
        var approve = new Button
        {
            Content = approveLabel,
            IsEnabled = requiredText is null
        };
        input.TextChanged += (_, _) => approve.IsEnabled = input.Text == requiredText;
        approve.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(approve);
        footer.Children.Add(buttons);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock
            {
                Text = content, TextWrapping = TextWrapping.Wrap
            }
        });
        window.Content = root;
        return window.ShowDialog() == true;
    }
}
