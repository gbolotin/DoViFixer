using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DoViFixer.App.ViewModels;
using DoViFixer.App.Views;

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

                    var navigation = (ListBox)shell.FindName("Navigation");
                    var workspace = (ContentControl)shell.FindName("Workspace");
                    if (navigation.Items.Count != 3 || workspace.Content is not MediaView media || !ReferenceEquals(media.DataContext, model.Media))
                    {
                        throw new InvalidOperationException("Workspace navigation did not initialize.");
                    }

                    var visited = new Dictionary<string, object>();
                    foreach (var (name, viewModel, viewType) in new (string, object, Type)[]
                    {
                        ("Archive", model.Archive, typeof(ArchiveView)),
                        ("Settings", model.Settings, typeof(SettingsView)),
                        ("Media", model.Media, typeof(MediaView)),
                        ("Archive", model.Archive, typeof(ArchiveView)),
                        ("Settings", model.Settings, typeof(SettingsView)),
                        ("Media", model.Media, typeof(MediaView))
                    }
                    )
                    {
                        navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().Single(item => Equals(item.Tag, name));
                        await WaitForAsync(() => workspace.Content is FrameworkElement view && view.GetType() == viewType && ReferenceEquals(view.DataContext, viewModel));
                        if (visited.TryGetValue(name, out var previous) && !ReferenceEquals(previous, workspace.Content))
                        {
                            throw new InvalidOperationException("Navigation recreated the view: " + name);
                        }

                        visited[name] = workspace.Content;
                        await WaitForAsync(() => model.CanNavigate);
                    }

                    if (!ReferenceEquals(media, workspace.Content))
                    {
                        throw new InvalidOperationException("Navigation lost the original media view.");
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

                    System.Console.WriteLine("WPF startup, three workspace views and navigation passed.");
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
}
