using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DoViFixer.App.ViewModels;
using DoViFixer.App.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Prism.Ioc;
using Prism.Navigation.Regions;
using DoViFixer.Application.Settings;

namespace DoViFixer.App.Tests;
[TestClass]
public sealed class VisualTests
{
    [TestMethod]
    public async Task RenderStudioViewsAndExerciseActiveBatchOnDispatcher()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await RenderAsync();
                    finished.SetResult();
                }
                catch (Exception ex)
                {
                    finished.SetException(ex);
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static async Task RenderAsync()
    {
        var app = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
            ThemeMode = ThemeMode.System
        };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DoViFixer.App;component/Resources/Common.xaml", UriKind.Relative)
        });
        var listener = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            using var runtime = new TestRuntime();
            ContainerLocator.SetContainerExtension((IContainerExtension)runtime.Container);
            var shell = runtime.Container.Resolve<ShellViewModel>();
            var model = shell.Media;
            var media = new MediaView(model);
            await RenderAsync(media, "01-media-empty");
            await model.AddAsync([@"D:\Media\Mountain.mkv", @"D:\Media\Ocean.mkv", @"D:\Media\City.mkv"]);
            await model.ScanCommand.ExecuteAsync();
            model.Focused = model.Files[1];
            await RenderAsync(media, "02-scan-results");
            model.UpdateSettingsSummary(new UserSettings
            {
                OutputDirectory = @"D:\Media\A very long destination folder\Another long folder\Converted movies",
                ReplaceOriginal = true,
                IncludeSimple = true,
                ForceComplex = true,
                CreateElArchive = true
            });
            var window = new Shell(shell, new RegionManager());
            var shellContent = (DockPanel)window.Content;
            var workspace = shellContent.Children.OfType<ContentControl>().Single();
            workspace.Content = media;
            media.ClearValue(FrameworkElement.WidthProperty);
            media.ClearValue(FrameworkElement.HeightProperty);
            // Exercise the actual shell layout, reserving space for window chrome at 800 x 600.
            await RenderAsync(shellContent, "09-shell-minimum", 780, 560);
            foreach (string caption in new[] { "⌕  Scan all", "▷  Convert to DV8.1", "▷  Convert to HDR10", "⚙ Settings" })
            {
                AssertInside(FindButton(shellContent, caption)!, shellContent);
            }
            AssertInside(FindText(shellContent, "⚠ Replace originals")!, shellContent);
            AssertInside(FindText(shellContent, "FEL: Simple + Complex")!, shellContent);
            workspace.Content = null;
            model.UpdateSettingsSummary(runtime.Settings);
            var completeAnalysis = model.Files[1].Analysis!;
            model.Files[1].Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(completeAnalysis.Media, completeAnalysis.Evidence with
            {
                SuccessfulSamples = 9,
                SampleDiagnostics = "Sample 10/10 at 01:39:51: No RPU was found in input file"
            });
            await RenderAsync(media, "02-incomplete-scan");
            model.Files[1].Analysis = completeAnalysis;
            foreach (var command in new[] { model.ScanCommand, model.InspectCommand, model.DeepInspectCommand })
            {
                await runtime.ClearAsync(default);
                model.ToggleSelectAllCommand.Execute();
                if (model.AllFilesSelected != true)
                {
                    model.ToggleSelectAllCommand.Execute();
                }

                var analyzing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseAnalysis = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                int analyses = 0;
                runtime.DuringAnalysis = async token =>
                {
                    if (++analyses == 2)
                    {
                        analyzing.SetResult();
                        await releaseAnalysis.Task.WaitAsync(token);
                    }
                };
                var analysisOperation = command.ExecuteAsync();
                await analyzing.Task;
                Assert.AreEqual(1, model.BatchProgress.Processed);
                Assert.AreEqual(3, model.BatchProgress.Total);
                await RenderAsync(media, $"02-{model.BatchProgress.Operation}-progress", 1060, 685);
                Assert.AreEqual(40, model.BatchProgress.CurrentJob!.Progress.StagePercent);
                releaseAnalysis.SetResult();
                await analysisOperation;
                Assert.AreEqual(100, model.BatchProgress.Percent);
                runtime.DuringAnalysis = null;
            }

            await runtime.ClearAsync(default);
            foreach (var row in model.Files)
            {
                row.IsSelected = true;
            }

            await runtime.UpdateAsync(s => s with { AllowFel = true }, default);
            await model.ScanCommand.ExecuteAsync();
            model.Files[2].IsSelected = true;
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var recovering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRecovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.DuringConversion = async token =>
            {
                if (runtime.Conversions != 1)
                {
                    return;
                }

                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                finally
                {
                    recovering.SetResult();
                    await releaseRecovery.Task;
                }
            };
            var operation = model.ConvertDv81Command.ExecuteAsync();
            await started.Task;
            model.Focused = model.Files[1];
            Assert.IsFalse(shell.CanNavigate);
            Assert.IsTrue(model.Files[0].IsActive);
            Assert.IsFalse(model.Files[0].SelectionEnabled);
            await RenderAsync(media, "05-conversion-progress");
            await RenderAsync(media, "05-conversion-progress-minimum", 1060, 685);
            Assert.AreSame(model.Files[0], model.BatchProgress.CurrentJob);
            Assert.AreEqual(45, model.Files[0].Progress.StagePercent);
            model.Files[1].IsSelected = false;
            Assert.AreEqual("Conversion skipped", model.Files[1].Status);
            var cancelJob = FindButton(media, "Cancel job")!;
            Assert.IsNotNull(cancelJob);
            Assert.AreSame(model.Files[0], cancelJob.CommandParameter);
            var peer = new ButtonAutomationPeer(cancelJob);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await recovering.Task;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual("Cancelling…", model.Files[0].Progress.Stage);
            Assert.IsFalse(cancelJob.IsEnabled);
            Assert.AreEqual(1, runtime.Conversions);
            releaseRecovery.SetResult();
            await operation;
            Assert.AreEqual(2, runtime.Conversions);
            Assert.AreEqual("Conversion cancelled", model.Files[0].Status);
            Assert.AreEqual("Converted", model.Files[2].Status);
            Assert.AreEqual(3, model.BatchProgress.Processed, "Cancelled, skipped and completed files all finish their batch slot.");
            Assert.AreEqual(100, model.BatchProgress.Percent);
            Assert.IsTrue(shell.CanNavigate);
            model.Focused = model.Files[2];
            await RenderAsync(media, "06-results");
            await RenderAsync(new ArchiveView(shell.Archive), "07-backup-restore");
            await shell.Settings.LoadCommand.ExecuteAsync();
            await shell.Settings.CheckCommand.ExecuteAsync();
            var settingsView = new SettingsView(shell.Settings);
            await RenderAsync(settingsView, "08-settings");
            shell.Settings.OtherFolder = true;
            await shell.Settings.SaveTask;
            var settingsScroll = ((DockPanel)settingsView.Content).Children.OfType<ScrollViewer>().Single();
            await RenderAsync(settingsView, "08-settings-save-error", 620, 560);
            Assert.AreEqual(Visibility.Visible, FindButton(settingsView, "Retry saving settings")!.Visibility);
            Assert.AreEqual(Visibility.Visible, FindText(settingsView, "Choose an output folder.")!.Visibility);
            AssertInside(FindButton(settingsView, "Retry saving settings")!, settingsView);
            AssertInside(FindButton(settingsView, "Discard unsaved changes")!, settingsView);
            settingsScroll.ScrollToEnd();
            await RenderAsync(settingsView, "08-settings-save-error-scrolled", 620, 560);
            AssertInside(FindButton(settingsView, "Discard unsaved changes")!, settingsView);

            shell.Settings.DiscardChangesCommand.Execute();
            Assert.IsFalse(shell.Settings.OtherFolder);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsFalse(FindButton(settingsView, "Retry saving settings")!.IsVisible);
            runtime.DuringDependencyCheck = token => Task.Delay(Timeout.Infinite, token);
            var checking = shell.Settings.CheckCommand.ExecuteAsync();
            settingsScroll.ScrollToEnd();
            await RenderAsync(settingsView, "08-settings-operation");
            var cancelSettings = FindButton(settingsView, "Cancel operation")!;
            Assert.AreEqual(Visibility.Visible, cancelSettings.Visibility);
            Assert.IsTrue(cancelSettings.IsEnabled);
            cancelSettings.Command.Execute(null);
            await checking;
            Assert.AreEqual("Cancelled; cleanup finished.", shell.Settings.Status);
            Assert.IsTrue(shell.CanNavigate);
            runtime.DuringDependencyCheck = null;
            Assert.AreEqual("", listener.Errors.ToString(), "WPF binding errors");
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            app.Shutdown();
            ContainerLocator.ResetContainer();
        }
    }

    private static Button? FindButton(DependencyObject parent, string content)
    {
        if (parent is Button button && Equals(button.Content, content))
        {
            return button;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (FindButton(VisualTreeHelper.GetChild(parent, i), content) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static void AssertInside(FrameworkElement element, FrameworkElement host)
    {
        Assert.IsNotNull(element);
        var bounds = element.TransformToAncestor(host).TransformBounds(new Rect(element.RenderSize));
        Assert.IsTrue(element.ActualWidth > 0 && element.ActualHeight > 0);
        Assert.IsTrue(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= host.ActualWidth + 1 && bounds.Bottom <= host.ActualHeight + 1,
            $"{element} is clipped: {bounds} inside {host.RenderSize}.");
    }

    private static TextBlock? FindText(DependencyObject parent, string text)
    {
        if (parent is TextBlock block && block.Text == text)
        {
            return block;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (FindText(VisualTreeHelper.GetChild(parent, i), text) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static async Task RenderAsync(FrameworkElement view, string name, int width = 1400, int height = 825)
    {
        view.Width = width;
        view.Height = height;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
        var backgroundVisual = new DrawingVisual();
        using (var dc = backgroundVisual.RenderOpen())
        {
            var brush = (Brush)System.Windows.Application.Current.FindResource("ApplicationBackgroundBrush");
            dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(backgroundVisual);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DoViFixer.sln")))
        {
            directory = directory.Parent;
        }

        string output = Path.Combine(directory!.FullName, "artifacts", "wpf");
        Directory.CreateDirectory(output);
        using var file = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(file);
    }

    private sealed class BindingErrors : TraceListener
    {
        public StringBuilder Errors
        {
            get;
        }
        = new();

        public override void Write(string? message) => Errors.Append(message);
        public override void WriteLine(string? message) => Errors.AppendLine(message);
    }
}
