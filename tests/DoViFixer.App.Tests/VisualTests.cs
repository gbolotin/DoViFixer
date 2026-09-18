using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DoViFixer.App.ViewModels;
using DoViFixer.App.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Prism.Ioc;

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
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DoViFixer.App;component/Resources/Studio.xaml", UriKind.Relative)
        });
        var listener = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            using var runtime = new TestRuntime();
            var shell = runtime.Container.Resolve<ShellViewModel>();
            var model = shell.Media;
            var media = new MediaView(model);
            await RenderAsync(media, "01-media-empty");
            await model.AddAsync([@"D:\Media\Mountain.mkv", @"D:\Media\Ocean.mkv", @"D:\Media\City.mkv"]);
            await model.ScanCommand.ExecuteAsync();
            model.Focused = model.Files[1];
            await RenderAsync(media, "02-scan-results");
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
                Assert.AreEqual(40, model.BatchProgress.StagePercent);
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

            await model.ScanCommand.ExecuteAsync();
            model.Files[2].IsSelected = true;
            await model.ConvertCommand.ExecuteAsync();
            await RenderAsync(media, "03-conversion-review");
            await RenderAsync(media, "04-review-minimum", 1060, 685);
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
            var operation = model.ApproveCommand.ExecuteAsync();
            await started.Task;
            model.Focused = model.Files[1];
            Assert.IsFalse(shell.CanNavigate);
            Assert.IsTrue(model.Files[0].IsActive);
            Assert.IsFalse(model.Files[0].SelectionEnabled);
            await RenderAsync(media, "05-conversion-progress");
            await RenderAsync(media, "05-conversion-progress-minimum", 1060, 685);
            Assert.AreSame(model.Files[0], model.BatchProgress.CurrentJob);
            Assert.AreEqual(45, model.BatchProgress.StagePercent);
            model.Files[1].IsSelected = false;
            Assert.AreEqual("Conversion skipped", model.Files[1].Status);
            model.CancelFileCommand.Execute(model.Files[0]);
            await recovering.Task;
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
            await RenderAsync(new SettingsView(shell.Settings), "08-settings");
            Assert.AreEqual("", listener.Errors.ToString(), "WPF binding errors");
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            app.Shutdown();
        }
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
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
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
