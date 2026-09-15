using System.Windows.Controls;
using DoViFixer.App.ViewModels;

namespace DoViFixer.App.Views;
public partial class ArchiveView : UserControl
{
    public ArchiveView(ArchiveViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}
