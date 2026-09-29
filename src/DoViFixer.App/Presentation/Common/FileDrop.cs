using System.Windows;
using System.Windows.Input;

namespace DoViFixer.App.Presentation.Common;

public static class FileDrop
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(FileDrop), new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        element.PreviewDragEnter -= OnDrag;
        element.PreviewDragOver -= OnDrag;
        element.PreviewDrop -= OnDrag;
        if (e.NewValue is ICommand)
        {
            element.PreviewDragEnter += OnDrag;
            element.PreviewDragOver += OnDrag;
            element.PreviewDrop += OnDrag;
        }
    }

    private static void OnDrag(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if ((e.AllowedEffects & DragDropEffects.Copy) == 0
            || !e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths
            || GetCommand((UIElement)sender) is not { } command
            || !command.CanExecute(paths))
        {
            return;
        }

        e.Effects = DragDropEffects.Copy;
        if (e.RoutedEvent == UIElement.PreviewDropEvent)
        {
            command.Execute(paths);
        }
    }
}
