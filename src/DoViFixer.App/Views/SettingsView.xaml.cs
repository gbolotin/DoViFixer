using System.Windows.Controls;
using DoViFixer.App.ViewModels;

namespace DoViFixer.App.Views;
public partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}
