using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DoViFixer.App.ViewModels;
using DoViFixer.App.Presentation;
using DoViFixer.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class VisualTests
{
    [TestMethod]
    public async Task ShellTemplatesSupportNavigationRowActionsAndShutdownRecovery()
    {
        // WPF allows only one Application per process. Exercise the real templates
        // together on one STA dispatcher, using fake media services throughout.
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await ExerciseShellAsync();
                    finished.TrySetResult();
                }
                catch (Exception ex)
                {
                    finished.TrySetException(ex);
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static async Task ExerciseShellAsync()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DoViFixer.App;component/Resources/Common.xaml", UriKind.Relative)
        });
        using var errors = new BindingErrors();
        var source = PresentationTraceSources.DataBindingSource;
        var previousLevel = source.Switch.Level;
        source.Listeners.Add(errors);
        source.Switch.Level = SourceLevels.Error;
        using var runtime = new TestRuntime();
        var window = runtime.Container.GetRequiredService<Shell>();
        var shell = (ShellViewModel)window.DataContext;
        var media = shell.Pages.OfType<MediaViewModel>().Single();
        window.Width = 1400;
        window.Height = 900;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        try
        {
            window.Show();
            await shell.InitializeAsync();
            await LayoutAsync(window);
            var navigation = Descendants<ListBox>(window).Single(list => ReferenceEquals(list.ItemsSource, shell.Pages));
            var pageHost = Descendants<ItemsControl>(window).Single(control => control.Name == "PageHost");
            var mediaView = (ContentPresenter)pageHost.ItemContainerGenerator.ContainerFromItem(media);
            Assert.AreSame(media, navigation.SelectedItem);
            Assert.HasCount(3, navigation.Items);

            await VerifyEmptyMediaAsync(runtime, media, window, navigation, mediaView);
            await media.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Mountain2.mkv"]);
            media.Focused = media.Files[1];
            media.Files[0].IsSelected = false;
            await LayoutAsync(window);
            var list = Descendants<ListView>(mediaView).Single();
            var fileColumn = ((GridView)list.View).Columns[0];
            double originalWidth = fileColumn.Width;
            fileColumn.Width = 400;
            var splitter = Descendants<GridSplitter>(mediaView).Single();
            var columns = ((Grid)splitter.Parent).ColumnDefinitions;
            var originalListWidth = columns[0].Width;
            var originalDetailsWidth = columns[2].Width;
            double listWidth = columns[0].ActualWidth;
            double detailsWidth = columns[2].ActualWidth;
            Assert.IsTrue(splitter.IsVisible && splitter.Focusable);
            DragSplitter(splitter, 80);
            await LayoutAsync(window);
            Assert.AreEqual(listWidth + 80, columns[0].ActualWidth, 1);
            Assert.AreEqual(detailsWidth - 80, columns[2].ActualWidth, 1);
            double resizedListWidth = columns[0].ActualWidth;
            double resizedDetailsWidth = columns[2].ActualWidth;
            var retainedRoots = new Dictionary<object, DependencyObject>
            {
                [media] = VisualTreeHelper.GetChild(mediaView, 0)
            };
            double? settingsOffset = null;
            foreach (var page in shell.Pages.Skip(1).Append(media).Concat(shell.Pages.Skip(1).Append(media)))
            {
                navigation.SelectedItem = page;
                await shell.NavigationTask;
                await LayoutAsync(window);
                var workspace = (ContentPresenter)pageHost.ItemContainerGenerator.ContainerFromItem(page);
                Assert.AreSame(page, shell.CurrentPage);
                Assert.AreSame(page, workspace.Content);
                Assert.IsTrue(Descendants<FrameworkElement>(workspace).Any(element => ReferenceEquals(element.DataContext, page) && element.ActualHeight > 0));
                var root = VisualTreeHelper.GetChild(workspace, 0);
                if (retainedRoots.TryGetValue(page, out var previousRoot))
                {
                    Assert.AreSame(previousRoot, root, "Navigation must retain each page's visual tree.");
                }
                retainedRoots[page] = root;
                var containers = shell.Pages.Select(item => (ContentPresenter)pageHost.ItemContainerGenerator.ContainerFromItem(item)).ToArray();
                Assert.AreEqual(1, containers.Count(container => container.IsVisible));
                Assert.IsTrue(workspace.IsVisible);
                foreach (var hidden in containers.Where(container => !ReferenceEquals(container, workspace)))
                {
                    Assert.AreEqual(Visibility.Collapsed, hidden.Visibility);
                    foreach (var control in Descendants<Control>(hidden).Where(control => control.Focusable && control.IsEnabled))
                    {
                        Assert.IsFalse(control.Focus(), "Controls on a collapsed page must not receive focus.");
                    }
                    Assert.IsFalse(hidden.IsKeyboardFocusWithin);
                }
                if (ReferenceEquals(page, shell.Settings))
                {
                    var scroll = Descendants<ScrollViewer>(workspace).Single(view => view.Content is StackPanel);
                    if (settingsOffset is { } offset)
                    {
                        Assert.AreEqual(offset, scroll.VerticalOffset, 0.1, "Settings must retain its scroll position.");
                    }
                    else
                    {
                        scroll.ScrollToEnd();
                        await LayoutAsync(window);
                        settingsOffset = scroll.VerticalOffset;
                        Assert.IsTrue(settingsOffset > 0, "The test must exercise a scrolled page.");
                    }
                }

                var navigationTask = shell.NavigationTask;
                navigation.UnselectAll();
                await LayoutAsync(window);
                Assert.AreSame(page, shell.CurrentPage, "Deselecting the sidebar must not clear the current page.");
                Assert.AreSame(page, workspace.Content);
                Assert.AreSame(page, navigation.SelectedItem, "The sidebar must restore its current selection.");
                Assert.AreSame(navigationTask, shell.NavigationTask, "Rejecting null must not start another navigation.");
            }
            Assert.AreSame(media.Files[1], media.Focused);
            Assert.IsFalse(media.Files[0].IsSelected);
            Assert.AreSame(list, Descendants<ListView>(mediaView).Single());
            Assert.AreEqual(400, fileColumn.Width, "Media must retain resized columns.");
            Assert.AreEqual(resizedListWidth, columns[0].ActualWidth, 1, "Navigation must retain the panel split.");
            Assert.AreEqual(resizedDetailsWidth, columns[2].ActualWidth, 1);
            DragSplitter(splitter, 10000);
            await LayoutAsync(window);
            Assert.AreEqual(280, columns[2].ActualWidth, 1, "Details must keep their minimum width.");
            DragSplitter(splitter, -10000);
            await LayoutAsync(window);
            Assert.AreEqual(240, columns[0].ActualWidth, 1, "The list must keep its minimum width.");
            columns[0].Width = originalListWidth;
            columns[2].Width = originalDetailsWidth;
            fileColumn.Width = originalWidth;
            Assert.AreSame(media.Files, list.ItemsSource);
            Assert.AreSame(media.Focused, list.SelectedItem);

            await VerifyRowActionsAsync(runtime, media, list, window, navigation, mediaView);
            window.Width = 800;
            window.Height = 600;
            await LayoutAsync(window);
            var content = (FrameworkElement)window.Content;
            AssertInside(splitter, content);
            foreach (string caption in new[] { "⌕  Scan all", "◎  Inspect", "◎  Deep Inspect", "▷  Convert to DV8.1", "▷  Convert to HDR10", "⚙ Settings" })
            {
                AssertInside(Button(mediaView, caption), content);
            }
            SaveRender(content, "media-minimum");

            Invoke(Button(mediaView, "⚙ Settings"));
            await LayoutAsync(window);
            await shell.NavigationTask;
            Assert.AreSame(shell.Settings, navigation.SelectedItem);
            var settingsView = (ContentPresenter)pageHost.ItemContainerGenerator.ContainerFromItem(shell.Settings);
            await VerifySettingsRecoveryAsync(runtime, shell, window, navigation, settingsView);
            Assert.AreEqual("", errors.Errors.ToString(), "WPF binding errors were reported.");
        }
        finally
        {
            source.Listeners.Remove(errors);
            source.Switch.Level = previousLevel;
            app.Shutdown();
        }
    }

    private static async Task VerifyEmptyMediaAsync(TestRuntime runtime, MediaViewModel media, Window window, ListBox navigation, DependencyObject mediaView)
    {
        var panel = Descendants<StackPanel>(mediaView).Single(element => element.Name == "EmptyMediaPanel");
        var status = Descendants<TextBlock>(mediaView).Single(element => element.Name == "MediaStatus");
        var selection = Descendants<TextBlock>(mediaView).Single(element => element.Name == "MediaSelectionSummary");
        var addFiles = Button(panel, "Add files");
        var addFolder = Button(panel, "Add folder");
        Assert.IsTrue(panel.IsVisible);
        Assert.IsFalse(status.IsVisible, "Ready must not clutter the empty state.");
        Assert.IsFalse(selection.IsVisible, "An empty list must not show a selection count.");
        Assert.IsTrue(navigation.IsVisible && navigation.IsEnabled);
        Assert.AreSame(media.AddFilesCommand, addFiles.Command);
        Assert.AreSame(media.AddFolderCommand, addFolder.Command);
        Assert.IsTrue(addFiles.IsEnabled && addFolder.IsEnabled);
        Assert.IsTrue(addFiles.Focus());
        Assert.IsTrue(addFolder.Focus());
        CollectionAssert.AreEquivalent(new[] { addFiles, addFolder }, Descendants<Button>(mediaView).Where(button => button.IsVisible).ToArray());

        // Visibility follows the enum even if a message matches former hidden text.
        foreach (string message in new[] { "Ready", "File list cleared." })
        {
            media.SetStatus(ViewStatus.Error, message);
            await LayoutAsync(window);
            Assert.IsTrue(status.IsVisible);
            Assert.AreEqual(message, status.Text);
        }
        media.SetStatus(ViewStatus.Cancelled);
        await LayoutAsync(window);
        Assert.IsTrue(status.IsVisible);
        Assert.AreEqual("Cancelled; cleanup finished.", status.Text);
        media.SetStatus(ViewStatus.Ready);
        await LayoutAsync(window);
        Assert.IsFalse(status.IsVisible);
        Assert.AreEqual("Ready", status.Text);

        window.Width = 800;
        window.Height = 600;
        await LayoutAsync(window);
        var content = (FrameworkElement)window.Content;
        AssertInside(panel, content);
        AssertInside(addFiles, content);
        AssertInside(addFolder, content);
        foreach (var text in Descendants<TextBlock>(panel).Where(text => text.IsVisible))
        {
            AssertInside(text, content);
        }
        SaveRender(content, "media-empty-minimum");

        // Cancelled pickers leave the same usable empty state.
        foreach (var button in new[] { addFiles, addFolder })
        {
            Invoke(button);
            await LayoutAsync(window);
            await media.Completion;
            Assert.IsEmpty(media.Files);
            Assert.IsTrue(panel.IsVisible);
            Assert.IsTrue(addFiles.IsEnabled && addFolder.IsEnabled);
        }

        runtime.PickedFolder = @"C:\Media";
        runtime.DiscoverFiles = _ => [];
        Invoke(addFolder);
        await LayoutAsync(window);
        await media.Completion;
        await LayoutAsync(window);
        Assert.IsEmpty(media.Files);
        Assert.IsTrue(panel.IsVisible);

        runtime.DiscoverFiles = _ => throw new IOException("Folder unavailable");
        Invoke(addFolder);
        await LayoutAsync(window);
        await media.Completion;
        await LayoutAsync(window);
        Assert.IsTrue(status.IsVisible);
        Assert.AreEqual(@"Could not add C:\Media: Folder unavailable", status.Text);
        AssertInside(status, content);
        SaveRender(content, "media-empty-error");

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        runtime.DiscoverFiles = _ =>
        {
            started.TrySetResult();
            // IFileDiscovery is synchronous; hold its worker while checking the UI.
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Discovery was not released by the visual test.");
            }
            return [];
        };
        var discovering = media.AddFolderCommand.ExecuteAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await LayoutAsync(window);
            Assert.IsTrue(panel.IsVisible);
            Assert.IsFalse(addFiles.IsEnabled || addFolder.IsEnabled);
            Assert.IsTrue(Descendants<ProgressBar>(mediaView).Any(progress => progress.IsVisible));
            var cancel = Button(mediaView, "Cancel batch");
            Assert.IsTrue(cancel.IsVisible);
            Assert.AreSame(media.CancelCommand, cancel.Command);
            AssertInside(cancel, content);
            Invoke(cancel);
            await LayoutAsync(window);
        }
        finally
        {
            release.Set();
            await discovering.WaitAsync(TimeSpan.FromSeconds(10));
            runtime.DiscoverFiles = null;
        }
        await LayoutAsync(window);
        Assert.IsTrue(addFiles.IsEnabled && addFolder.IsEnabled);

        runtime.PickedFiles = [@"C:\Media\Mountain.mkv"];
        Invoke(addFiles);
        await LayoutAsync(window);
        await media.Completion;
        await LayoutAsync(window);
        Assert.HasCount(1, media.Files);
        Assert.IsFalse(panel.IsVisible);
        Assert.IsTrue(selection.IsVisible);
        Assert.AreEqual(media.SelectionSummary, selection.Text);
        media.SetStatus(ViewStatus.Error, "Media operation failed.");
        await LayoutAsync(window);
        Assert.IsTrue(status.IsVisible);
        Assert.AreEqual(media.StatusMessage, status.Text, "Populated and empty lists must use the same status element.");
        AssertInside(status, content);
        Assert.IsFalse(addFiles.Focus());
        var list = Descendants<ListView>(mediaView).Single();
        Assert.IsTrue(list.IsVisible);
        var fileColumn = ((GridView)list.View).Columns[0];
        double originalWidth = fileColumn.Width;
        fileColumn.Width = 400;

        Invoke(Button(mediaView, "Clear all"));
        await LayoutAsync(window);
        Assert.IsEmpty(media.Files);
        Assert.IsTrue(panel.IsVisible);
        Assert.IsFalse(status.IsVisible, "Clearing should restore the neutral empty state.");
        Assert.AreEqual(ViewStatus.FileListCleared, media.Status);
        Assert.AreEqual("File list cleared.", status.Text);
        Assert.IsFalse(selection.IsVisible);
        Assert.IsFalse(list.IsVisible);
        CollectionAssert.AreEquivalent(new[] { addFiles, addFolder }, Descendants<Button>(mediaView).Where(button => button.IsVisible).ToArray());
        foreach (var hidden in Descendants<Control>(mediaView).Where(control => !control.IsVisible && control.Focusable && control.IsEnabled))
        {
            Assert.IsFalse(hidden.Focus(), "Hidden media controls must not receive focus.");
        }

        Invoke(addFolder);
        await LayoutAsync(window);
        await media.Completion;
        await LayoutAsync(window);
        Assert.HasCount(1, media.Files);
        Assert.IsFalse(panel.IsVisible);
        Assert.IsTrue(list.IsVisible);
        Assert.AreSame(list, Descendants<ListView>(mediaView).Single());
        Assert.AreEqual(400, fileColumn.Width, "Clearing and adding must retain column widths.");
        fileColumn.Width = originalWidth;
        media.ClearAllCommand.Execute();
        runtime.PickedFiles = [];
        runtime.PickedFolder = null;
        window.Width = 1400;
        window.Height = 900;
        await LayoutAsync(window);
    }

    private static async Task VerifyRowActionsAsync(TestRuntime runtime, MediaViewModel media, ListView list, Window window, ListBox navigation, DependencyObject mediaView)
    {
        await LayoutAsync(window);
        var row = media.Files[0];
        var container = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(row);
        var remove = Descendants<Button>(container).Single(button => Equals(button.ToolTip, "Remove from list"));
        Assert.AreSame(media.RemoveFileCommand, remove.Command);
        Assert.AreSame(row, remove.CommandParameter);
        Assert.AreEqual("Remove from list", System.Windows.Automation.AutomationProperties.GetName(remove));
        Assert.AreEqual("\uE74D", ((TextBlock)remove.Content).Text);
        Assert.IsFalse(remove.IsVisible);
        container.IsSelected = true;
        await LayoutAsync(window);
        Assert.IsTrue(remove.IsVisible, "Selecting a row must show delete even without hovering.");
        container.IsSelected = false;
        media.Focused = media.Files[1];
        await LayoutAsync(window);
        Assert.IsFalse(remove.IsVisible, "An unselected row without hover must hide delete.");
        foreach (var (caption, command) in new (string, object)[]
        {
            ("Cancel", media.CancelFileCommand), ("Retry", media.RetryAnalysisCommand),
            ("Inspect", media.InspectIncompleteCommand), ("Open folder", media.OpenRowOutputCommand), ("Skip", media.SkipCommand)
        })
        {
            var button = Button(container, caption);
            Assert.AreSame(command, button.Command, caption + " must resolve through the actual page template.");
            Assert.AreSame(row, button.CommandParameter);
        }

        // A failed analysis must be retryable from the row even when another row is focused.
        await runtime.ClearAsync(default);
        runtime.DuringAnalysis = _ => throw new IOException("Analysis unavailable");
        await media.ScanCommand.ExecuteAsync();
        runtime.DuringAnalysis = null;
        Assert.IsTrue(row.CanRetryAnalysis);
        await LayoutAsync(window);
        Invoke(Button(container, "Retry"));
        await LayoutAsync(window);
        await media.Completion;
        Assert.IsFalse(row.CanRetryAnalysis);
        Assert.IsTrue(media.Files[1].CanRetryAnalysis, "Retry must affect only the clicked row.");

        var analysis = row.Analysis!;
        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(analysis.Media, analysis.Evidence with { SuccessfulSamples = 9 });
        await LayoutAsync(window);
        int fullAnalyses = runtime.FullAnalyses;
        Invoke(Button(container, "Inspect"));
        await LayoutAsync(window);
        await media.Completion;
        Assert.AreEqual(fullAnalyses + 1, runtime.FullAnalyses);
        Assert.IsFalse(row.CanInspectIncomplete);

        media.Files[1].IsSelected = true;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringConversion = async token =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                recovering.TrySetResult();
                await release.Task;
            }
        };
        var converting = media.ConvertDv81Command.ExecuteAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await LayoutAsync(window);
            Assert.IsFalse(navigation.IsEnabled);
            Assert.AreSame(row, media.BatchProgress.CurrentJob);
            Assert.AreEqual(45, row.Progress.StagePercent);
            var pending = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(media.Files[1]);
            Invoke(Button(pending, "Skip"));
            await LayoutAsync(window);
            Assert.AreEqual("Conversion skipped", media.Files[1].Status);
            Assert.IsFalse(media.Files[1].IsPending);
            var cancel = Button(container, "Cancel");
            Invoke(cancel);
            await recovering.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await LayoutAsync(window);
            Assert.AreEqual("Cancelling…", row.Progress.Stage);
            Assert.IsFalse(cancel.IsEnabled);
            Assert.IsFalse(Button(mediaView, "Cancel job").IsEnabled);
            SaveRender((FrameworkElement)window.Content, "cancelling-job");
        }
        finally
        {
            media.CancelCommand.Execute();
            release.TrySetResult();
            await converting.WaitAsync(TimeSpan.FromSeconds(10));
            runtime.DuringConversion = null;
        }

        row.IsSelected = true;
        await media.ConvertDv81Command.ExecuteAsync();
        await LayoutAsync(window);
        Invoke(Button(container, "Open folder"));
        await LayoutAsync(window);
        Assert.AreEqual(@"C:\Media", runtime.OpenedFolder);
    }

    private static async Task VerifySettingsRecoveryAsync(TestRuntime runtime, ShellViewModel shell, Window window, ListBox navigation, DependencyObject settingsView)
    {
        shell.Settings.OtherFolder = true;
        await shell.Settings.SaveTask;
        await LayoutAsync(window);
        var discard = Button(settingsView, "Discard unsaved changes");
        Assert.IsTrue(discard.IsVisible);
        AssertInside(discard, (FrameworkElement)window.Content);
        var scroll = Descendants<ScrollViewer>(settingsView).Single(view => view.Content is StackPanel);
        scroll.ScrollToEnd();
        await LayoutAsync(window);
        AssertInside(discard, (FrameworkElement)window.Content);
        SaveRender((FrameworkElement)window.Content, "settings-save-error");

        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();
        await LayoutAsync(window);
        Assert.IsFalse(closed, "A failed settings save must leave the window open for recovery.");
        Assert.IsTrue(window.IsEnabled);
        Assert.AreEqual(ViewStatus.Error, shell.Settings.Status);
        Assert.AreEqual("Choose an output folder.", shell.Settings.StatusMessage);
        Invoke(discard);
        await LayoutAsync(window);
        Assert.IsNull(shell.Settings.SaveError);
        Assert.IsFalse(shell.Settings.OtherFolder);

        runtime.DuringDependencyCheck = token => Task.Delay(Timeout.Infinite, token);
        var checking = shell.Settings.CheckCommand.ExecuteAsync();
        try
        {
            await LayoutAsync(window);
            Assert.IsFalse(navigation.IsEnabled);
            var cancel = Button(settingsView, "Cancel operation");
            Assert.IsTrue(cancel.IsVisible);
            Invoke(cancel);
            await LayoutAsync(window);
            await checking.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(ViewStatus.Cancelled, shell.Settings.Status);
            var status = Descendants<TextBlock>(settingsView).Single(text => text.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path == nameof(OperationViewModel.StatusText));
            Assert.AreEqual("Cancelled; cleanup finished.", status.Text);
        }
        finally
        {
            shell.Settings.CancelCommand.Execute();
            await checking.WaitAsync(TimeSpan.FromSeconds(10));
        }
        window.Close();
        await LayoutAsync(window);
        Assert.IsTrue(closed, "Closing must succeed after recovery.");
    }

    private static void DragSplitter(GridSplitter splitter, double horizontalChange)
    {
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(horizontalChange, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new DragCompletedEventArgs(horizontalChange, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
    }

    private static Button Button(DependencyObject parent, string caption) => Descendants<Button>(parent).Single(button => Equals(button.Content, caption));

    private static void Invoke(Button button)
    {
        Assert.IsTrue(button.IsEnabled, $"{button.Content} must be enabled.");
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
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

    private static async Task LayoutAsync(Window window)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void AssertInside(FrameworkElement element, FrameworkElement host)
    {
        var bounds = element.TransformToAncestor(host).TransformBounds(new Rect(element.RenderSize));
        Assert.IsTrue(element.ActualWidth > 0 && element.ActualHeight > 0);
        Assert.IsTrue(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= host.ActualWidth + 1 && bounds.Bottom <= host.ActualHeight + 1,
            $"{element} is clipped: {bounds} inside {host.RenderSize}.");
    }

    private static void SaveRender(FrameworkElement content, string name)
    {
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DoViFixer.sln")))
        {
            root = root.Parent;
        }
        Assert.IsNotNull(root);
        string output = Path.Combine(root.FullName, "artifacts", "wpf-template-navigation");
        Directory.CreateDirectory(output);
        using var file = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(file);
    }

    private sealed class BindingErrors : TraceListener
    {
        public StringBuilder Errors { get; } = new();
        public override void Write(string? message) => Errors.Append(message);
        public override void WriteLine(string? message) => Errors.AppendLine(message);
    }
}
