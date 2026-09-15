using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DoViFixer.App.ViewModels;
using Prism.Navigation.Regions;

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
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    var shell = app.MainWindow ?? throw new InvalidOperationException("Prism did not create a shell.");
                    if (shell.DataContext is not ShellViewModel model || !model.CanNavigate)
                    {
                        throw new InvalidOperationException("Invalid idle shell state.");
                    }

                    var region = RegionManager.GetRegionManager(shell).Regions["Workspace"];
                    if (region.Views.Count() != 3 || region.ActiveViews.Count() != 1)
                    {
                        throw new InvalidOperationException("Workspace navigation did not initialize.");
                    }

                    foreach (string name in new[]
                    {
                        "Media",
                        "Archive",
                        "Settings"
                    }

                    )
                    {
                        region.Activate(region.GetView(name));
                        if (!region.ActiveViews.Contains(region.GetView(name)))
                        {
                            throw new InvalidOperationException("Navigation failed: " + name);
                        }
                    }

                    region.Activate(region.GetView("Media"));
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

                    System.Console.WriteLine("Prism startup, three workspace views and navigation passed.");
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
}
