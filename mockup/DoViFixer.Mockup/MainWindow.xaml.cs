using System.Windows;
using System.Windows.Controls;
using DoViFixer.Mockup.ViewModels;
using DoViFixer.Mockup.Views;

namespace DoViFixer.Mockup;
public partial class MainWindow : Window
{
    private readonly MockWorkspace model = new();
    public MainWindow()
    {
        InitializeComponent();
        ShowVariant();
        Closed += (_, _) => model.Dispose();
    }

    private void OnVariantChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VariantHost is not null)
        {
            ShowVariant();
        }
    }

    private void ShowVariant()
    {
        FrameworkElement view = VariantPicker.SelectedIndex switch
        {
            1 => new StudioView(),
            2 => new GuidedView(),
            _ => new FluentView()
        };
        view.DataContext = model;
        VariantHost.Content = view;
    }
}
