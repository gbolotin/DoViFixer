using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DoViFixer.App.ViewModels;

namespace DoViFixer.App.StartupCheck;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            System.Console.Error.WriteLine("Supply an output directory for the startup check.");
            return 2;
        }

        string output = Path.GetFullPath(args[0]);
        Environment.SetEnvironmentVariable("DoViFixer__DataDirectory", Path.Combine(output, "data"));
        try
        {
            var app = new DoViFixer.App.App();
            app.InitializeComponent();
            int result = 1;
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
            {
                try
                {
                    var shell = app.MainWindow ?? throw new InvalidOperationException("WPF did not create a shell.");
                    if (shell.DataContext is not ShellViewModel model || !model.CanNavigate)
                    {
                        throw new InvalidOperationException("Invalid idle shell state.");
                    }

                    var media = model.Pages.OfType<MediaViewModel>().Single();
                    await WaitForAsync(() => media.OutputSummaryToolTip.Length > 0);
                    shell.UpdateLayout();
                    var navigation = Descendants<ListBox>(shell).Single(list => ReferenceEquals(list.ItemsSource, model.Pages));
                    var pageHost = Descendants<ItemsControl>(shell).Single(control => control.Name == "PageHost");
                    var mediaView = (ContentPresenter)pageHost.ItemContainerGenerator.ContainerFromItem(media);
                    if (navigation.Items.Count != 3 || !mediaView.IsVisible || !ReferenceEquals(navigation.SelectedItem, media))
                    {
                        throw new InvalidOperationException("Workspace navigation did not initialize.");
                    }

                    var retainedRoots = new Dictionary<object, DependencyObject>
                    {
                        [media] = VisualTreeHelper.GetChild(mediaView, 0)
                    };
                    foreach (var page in model.Pages.Skip(1).Append(media).Concat(model.Pages.Skip(1).Append(media)))
                    {
                        navigation.SelectedItem = page;
                        var workspace = (ContentPresenter)pageHost.ItemContainerGenerator.ContainerFromItem(page);
                        await WaitForAsync(() => ReferenceEquals(model.CurrentPage, page) && workspace.IsVisible && model.CanNavigate);
                        shell.UpdateLayout();
                        var containers = model.Pages.Select(item => (ContentPresenter)pageHost.ItemContainerGenerator.ContainerFromItem(item));
                        if (containers.Count(container => container.IsVisible) != 1)
                        {
                            throw new InvalidOperationException("Exactly one page must be visible.");
                        }
                        var root = VisualTreeHelper.GetChild(workspace, 0);
                        if (retainedRoots.TryGetValue(page, out var previousRoot) && !ReferenceEquals(previousRoot, root))
                        {
                            throw new InvalidOperationException("Navigation recreated the page controls: " + page.NavigationName);
                        }
                        retainedRoots[page] = root;
                        bool rendered = page is MediaViewModel
                            ? Descendants<ListView>(workspace).Any(list => ReferenceEquals(list.ItemsSource, media.Files))
                            : Descendants<TextBlock>(workspace).Any(text => text.Text == page.NavigationName && ReferenceEquals(text.DataContext, page));
                        if (!rendered)
                        {
                            throw new InvalidOperationException("Page template did not render: " + page.NavigationName);
                        }
                    }

                    if (!ReferenceEquals(mediaView, pageHost.ItemContainerGenerator.ContainerFromItem(media)) || !mediaView.IsVisible)
                    {
                        throw new InvalidOperationException("Navigation lost the original media page.");
                    }
                    shell.UpdateLayout();
                    var content = (FrameworkElement)shell.Content;
                    var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    Directory.CreateDirectory(output);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var file = File.Create(Path.Combine(output, "shell.png")))
                    {
                        encoder.Save(file);
                    }

                    System.Console.WriteLine("WPF startup, three retained workspace views and navigation passed.");
                    result = 0;
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine(ex);
                }
                finally
                {
                    app.Shutdown();
                }
            }));
            app.Run();
            return result;
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }

        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
