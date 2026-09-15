using System.Windows;
using System.Windows.Controls;
using DoViFixer.App.ViewModels;

namespace DoViFixer.App.Views;
public partial class MediaView : UserControl
{
    public MediaView(MediaViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    private void ShowInspectionMenu(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.ContextMenu.DataContext = DataContext;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }
}
