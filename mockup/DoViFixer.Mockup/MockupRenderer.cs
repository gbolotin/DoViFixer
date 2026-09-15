using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DoViFixer.Mockup.ViewModels;
using DoViFixer.Mockup.Views;

namespace DoViFixer.Mockup;
// Explicit developer preview export; the interactive demo never accesses media files.
internal static class MockupRenderer
{
    public static void Render(string directory)
    {
        Directory.CreateDirectory(directory);
        using var model = new MockWorkspace();
        (string Name, FrameworkElement View)[] views = [("fluent", new FluentView()), ("studio", new StudioView()), ("guided", new GuidedView())];
        foreach (var(name, view)in views)
        {
            view.DataContext = model;
            view.Measure(new Size(1440, 1000));
            view.Arrange(new Rect(0, 0, 1440, 1000));
            view.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1440, 1000, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(stream);
        }
    }
}
