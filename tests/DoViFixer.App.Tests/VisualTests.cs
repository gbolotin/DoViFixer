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
using DoViFixer.App.Presentation.Application;
using DoViFixer.App.Presentation.Common;
using DoViFixer.App.Views;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
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
        runtime.MissingTools.Add(DoViFixer.Application.Dependencies.NativeTool.FFmpeg);
        byte[] preview = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_PREVIEW_OUTPUT") is { } previewPath
            ? File.ReadAllBytes(previewPath) : MediaPreviewTests.CreateImage();
        runtime.Preview = (_, _) => Task.FromResult(preview);
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
            int uiThread = Environment.CurrentManagedThreadId;
            var checkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.DuringDependencyCheck = async token =>
            {
                Assert.AreNotEqual(uiThread, Environment.CurrentManagedThreadId, "Dependency detection must run off the UI thread.");
                checkStarted.TrySetResult();
                await releaseCheck.Task.WaitAsync(token);
            };
            var initializing = shell.InitializeAsync();
            try
            {
                await checkStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await LayoutAsync(window);
                var cancelCheck = Descendants<Button>(window).Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Cancel dependency operation");
                AssertInside(cancelCheck, (FrameworkElement)window.Content);
                Assert.IsTrue(cancelCheck.IsEnabled);
                SaveRender((FrameworkElement)window.Content, "startup-dependency-check");
            }
            finally
            {
                runtime.DuringDependencyCheck = null;
                releaseCheck.TrySetResult();
                await initializing;
            }
            await LayoutAsync(window);
            window.Width = 800;
            window.Height = 600;
            await LayoutAsync(window);
            var warning = Descendants<StackPanel>(window).Single(panel => System.Windows.Automation.AutomationProperties.GetName(panel) == "Dependency warning");
            var warningIcon = Descendants<TextBlock>(warning).Single(text => text.Text == "\uE7BA");
            Assert.AreEqual(window.FindResource("SystemFillColorCautionBrush"), warningIcon.Foreground);
            Assert.AreEqual(window.FindResource("SymbolThemeFontFamily"), warningIcon.FontFamily);
            AssertInside(warning, (FrameworkElement)window.Content);
            StringAssert.Contains((string)warning.ToolTip, "FFmpeg: Missing");
            Assert.AreEqual(0, runtime.Installations);
            SaveRender((FrameworkElement)window.Content, "startup-dependency-warning");
            runtime.MissingTools.Clear();
            await shell.Settings.CheckCommand.ExecuteAsync();
            await LayoutAsync(window);
            Assert.IsFalse(Descendants<StackPanel>(window).Any(panel => System.Windows.Automation.AutomationProperties.GetName(panel) == "Dependency warning"));
            window.Width = 1400;
            window.Height = 900;
            await LayoutAsync(window);
            var navigation = Descendants<ListBox>(window).Single(list => ReferenceEquals(list.ItemsSource, shell.Pages));
            var pageHost = Descendants<ItemsControl>(window).Single(control => control.Name == "PageHost");
            var mediaView = (ContentPresenter)pageHost.ItemContainerGenerator.ContainerFromItem(media);
            Assert.AreSame(media, navigation.SelectedItem);
            Assert.HasCount(3, navigation.Items);
            VerifyCommandIcons(mediaView);
            SaveRender((FrameworkElement)window.Content, "command-icons-empty");

            await VerifyEmptyMediaAsync(runtime, media, window, navigation, mediaView);
            await VerifyFileDropAsync(runtime, media, window, mediaView);
            shell.Pages.OfType<ArchiveViewModel>().Single().SetStatus(ViewStatus.Result, "Archive status retained.");
            await LayoutAsync(window);
            Assert.AreEqual(media.StatusText, StatusItem(window, 1).Text, "Inactive pages must not overwrite the visible status.");
            await media.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Mountain2.mkv"]);
            media.Focused = media.Files[1];
            media.Files[0].IsSelected = false;
            await LayoutAsync(window);
            var list = Descendants<ListView>(mediaView).Single();
            Assert.AreEqual("2 items | 1 item selected", StatusItem(window, 0).Text);
            await VerifyClassificationColorsAsync(runtime, list);
            await VerifyLastColumnSizingAsync(list, window);
            await VerifyHeaderSortingAsync(media, list, window);
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
            runtime.AnalysisCacheBytes = 262144;
            runtime.FrameCacheBytes = 10485760;
            foreach (var page in shell.Pages.Skip(1).Append(media).Concat(shell.Pages.Skip(1).Append(media)))
            {
                navigation.SelectedItem = page;
                await shell.NavigationTask;
                await LayoutAsync(window);
                var workspace = (ContentPresenter)pageHost.ItemContainerGenerator.ContainerFromItem(page);
                Assert.AreSame(page, shell.CurrentPage);
                var statusBar = Descendants<StatusBar>(window).Single();
                CollectionAssert.AreEqual(page.StatusItems.Select(item => item.Text).ToArray(), Descendants<TextBlock>(statusBar).Select(text => text.Text).ToArray());
                AssertInside(statusBar, (FrameworkElement)window.Content);
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
                        var cacheSize = Descendants<TextBlock>(workspace).Single(text => text.Text == shell.Settings.CacheSizeText);
                        var cacheLocation = Descendants<TextBlock>(workspace).Single(text => text.Text == "Location: " + shell.Settings.CacheDirectory);
                        var cacheCard = (FrameworkElement)((FrameworkElement)cacheSize.Parent).Parent;
                        cacheCard.BringIntoView();
                        await LayoutAsync(window);
                        Assert.IsTrue(cacheSize.IsVisible);
                        AssertInside(cacheSize, (FrameworkElement)window.Content);
                        AssertInside(cacheLocation, (FrameworkElement)window.Content);
                        SaveRender((FrameworkElement)window.Content, "settings-cache-size");
                        var toolDescriptions = Descendants<TextBlock>(workspace).Single(text => text.Text == DependencySetup.ToolDescriptions);
                        toolDescriptions.BringIntoView();
                        await LayoutAsync(window);
                        AssertInside(toolDescriptions, (FrameworkElement)window.Content);
                        SaveRender((FrameworkElement)window.Content, "settings-tool-descriptions");
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

            await LayoutAsync(window);
            VerifyCommandIcons(mediaView);
            for (int attempt = 0; media.FramePreview is null && attempt < 100; attempt++)
            {
                await Task.Delay(20);
            }
            await LayoutAsync(window);
            var frame = Descendants<Image>(mediaView).Single(image => System.Windows.Automation.AutomationProperties.GetName(image) == "Movie frame preview");
            Assert.IsNotNull(media.FramePreview);
            Assert.AreSame(media.FramePreview, frame.Source);
            Assert.IsTrue(frame.IsVisible);
            Assert.IsTrue(frame.ActualHeight <= 240);
            var detailValues = Descendants<TextBlock>(mediaView)
                .Where(text => text.DataContext is KeyValuePair<string, string> && Grid.GetColumn(text) == 1).ToArray();
            Assert.AreEqual(media.Focused!.DetailRows.Count, detailValues.Length);
            foreach (var value in detailValues)
            {
                Assert.AreEqual(TextAlignment.Right, value.TextAlignment);
                Assert.AreEqual(value.Text, value.ToolTip);
            }
            SaveRender((FrameworkElement)window.Content, "command-icons-toolbar");
            await VerifyRowActionsAsync(runtime, media, list, window, navigation, mediaView);
            window.Width = 800;
            window.Height = 600;
            await LayoutAsync(window);
            var content = (FrameworkElement)window.Content;
            AssertInside(splitter, content);
            foreach (string caption in new[] { "Rescan", "Inspect", "Deep Inspect", "Convert to DV8.1", "Convert to HDR10" })
            {
                AssertInside(Descendants<Button>(mediaView).Single(button => Equals(button.Content, caption) && button.IsVisible), content);
            }
            SaveRender(content, "media-minimum");

            var mediaStatusBar = Descendants<StatusBar>(window).Single();
            foreach (string summary in new[] { media.OutputSummary, media.RetentionSummary, media.FelSummary, media.ArchiveSummary })
            {
                AssertInside(Descendants<TextBlock>(mediaStatusBar).Single(text => text.Text == summary), content);
                Assert.IsFalse(Descendants<TextBlock>(mediaView).Any(text => text.Text == summary));
            }

            Assert.IsFalse(Descendants<Button>(mediaView).Any(button => Equals(button.Content, "Settings")));
            navigation.SelectedItem = shell.Settings;
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

    private static async Task VerifyClassificationColorsAsync(TestRuntime runtime, ListView list)
    {
        var row = new MediaRow(@"C:\Media\Classification.mkv");
        var text = (TextBlock)((GridView)list.View).Columns[1].CellTemplate.LoadContent();
        text.DataContext = row;
        var media = await runtime.ProbeAsync(row.Path, default);
        var evidence = new RpuEvidence(AnalysisMethod.FullRpu, EnhancementLayer.Mel, 1000, null, 1, 1);

        async Task AssertColorAsync(string expected)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual((Color)ColorConverter.ConvertFromString(expected), ((SolidColorBrush)text.Foreground).Color);
        }

        await AssertColorAsync("#A6B6C3");
        foreach (var (verdict, color) in new[]
        {
            (AnalysisVerdict.Mel, "#66D94B"),
            (AnalysisVerdict.SimpleFel, "#29AEFA"),
            (AnalysisVerdict.ComplexFel, "#FF625A"),
            (AnalysisVerdict.AnalysisFailed, "#FF625A"),
            (AnalysisVerdict.Unknown, "#FFD166"),
            (AnalysisVerdict.FelUnclassified, "#FFD166"),
            (AnalysisVerdict.NotApplicable, "#A6B6C3")
        })
        {
            row.Analysis = new MediaAnalysis(media, evidence, verdict, "Test classification");
            await AssertColorAsync(color);
            row.AnalysisError = "";
            await AssertColorAsync("#FF625A");
            row.AnalysisError = null;
            await AssertColorAsync(color);
        }
        row.Analysis = null;
        await AssertColorAsync("#A6B6C3");
        row.AnalysisError = "Analysis failed";
        await AssertColorAsync("#FF625A");
    }

    private static async Task VerifyHeaderSortingAsync(MediaViewModel media, ListView list, Window window)
    {
        var grid = (GridView)list.View;
        GridViewColumnHeader Header(int column) => Descendants<GridViewColumnHeader>(list).Single(header => ReferenceEquals(header.Column, grid.Columns[column]));
        TextBlock Indicator(GridViewColumnHeader header) => Descendants<TextBlock>(header).Single(text => text.Style == list.FindResource("ColumnSortIndicator"));
        string ascending = char.ConvertFromUtf32(0xE70E);
        string descending = char.ConvertFromUtf32(0xE70D);
        var fileHeader = Header(0);
        var profileHeader = Header(1);
        var selectAll = Descendants<CheckBox>(fileHeader).Single();
        var original = media.Files.ToArray();
        var focused = media.Focused;
        Assert.AreSame(list.FindResource("SortableColumnHeader"), profileHeader.ContentTemplate, "Columns without a header template use the shared sortable header.");
        Assert.IsTrue(Descendants<TextBlock>(profileHeader).Any(text => text.Text == "Profile / Type"));
        Assert.IsFalse(Indicator(fileHeader).IsVisible);

        fileHeader.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, fileHeader));
        fileHeader.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, fileHeader));
        await LayoutAsync(window);
        CollectionAssert.AreEqual(Enumerable.Reverse(original).ToArray(), list.Items.Cast<MediaRow>().ToArray(), "Clicking a header twice sorts descending.");
        CollectionAssert.AreEqual(original, media.Files.ToArray(), "Sorting uses the collection view, not the source order.");
        Assert.AreSame(focused, list.SelectedItem, "Sorting keeps the list selection.");
        Assert.AreEqual(System.ComponentModel.ListSortDirection.Descending, GridViewSort.GetDirection(grid.Columns[0]));
        Assert.AreEqual(descending, Indicator(fileHeader).Text);
        Assert.IsTrue(Indicator(fileHeader).IsVisible, "Custom header templates show the shared indicator.");
        Assert.IsFalse(Indicator(profileHeader).IsVisible, "Only the sorted column shows an indicator.");

        selectAll.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, selectAll));
        Assert.AreEqual(System.ComponentModel.ListSortDirection.Descending, media.FileSort.Direction, "The select-all check box must not sort.");

        profileHeader.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, profileHeader));
        await LayoutAsync(window);
        Assert.AreEqual(MediaSortColumn.Classification, media.FileSort.Column);
        Assert.IsNull(GridViewSort.GetDirection(grid.Columns[0]));
        Assert.IsFalse(Indicator(fileHeader).IsVisible);
        Assert.AreEqual(ascending, Indicator(profileHeader).Text);
        Assert.IsTrue(Indicator(profileHeader).IsVisible, "The shared header template shows the indicator.");

        // Leave the list sorted by name ascending, which matches the order the files were added.
        fileHeader.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, fileHeader));
        await LayoutAsync(window);
        CollectionAssert.AreEqual(original, list.Items.Cast<MediaRow>().ToArray());
    }

    private static async Task VerifyLastColumnSizingAsync(ListView list, Window window)
    {
        var grid = (GridView)list.View;
        var scroll = Descendants<ScrollViewer>(list).First();
        double originalWindowWidth = window.Width;
        double originalColumnWidth = grid.Columns[0].Width;
        var originalPadding = list.Padding;
        var originalScrollVisibility = scroll.VerticalScrollBarVisibility;
        window.Width = 1800;
        await LayoutAsync(window);
        Assert.IsTrue(grid.Columns[^1].Width > 255, "The last column must expand into spare space.");
        Assert.AreEqual(0, scroll.ScrollableWidth, 0.1, "Filling must not introduce horizontal overflow.");

        double wideWidth = grid.Columns[^1].Width;
        grid.Columns[0].Width += 80;
        await LayoutAsync(window);
        Assert.AreEqual(wideWidth - 80, grid.Columns[^1].Width, 0.1, "Resizing another column must resize the last one.");

        window.Width -= 100;
        await LayoutAsync(window);
        Assert.IsTrue(grid.Columns[^1].Width < wideWidth - 80, "The last column must shrink with the window.");
        double withoutScrollbar = grid.Columns[^1].Width;
        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        await LayoutAsync(window);
        Assert.IsTrue(grid.Columns[^1].Width < withoutScrollbar, "Reserve space for the Fluent scrollbar overlay.");

        list.Padding = new Thickness(13, 0, 19, 0);
        await LayoutAsync(window);
        Assert.AreEqual(0, scroll.ScrollableWidth, 0.1, "List and row padding must not cause horizontal overflow.");
        var viewport = (ScrollContentPresenter)scroll.Template.FindName("PART_ScrollContentPresenter", scroll);
        var bar = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
        var row = Descendants<GridViewRowPresenter>(list).First();
        double columnsRight = row.TranslatePoint(new Point(grid.Columns.Sum(column => column.ActualWidth), 0), viewport).X;
        double scrollbarLeft = bar.TranslatePoint(new Point(), viewport).X;
        Assert.IsTrue(columnsRight <= scrollbarLeft && scrollbarLeft - columnsRight < 20,
            $"Columns must fill the usable viewport without overlapping the scrollbar: {columnsRight} / {scrollbarLeft}.");
        SaveRender((FrameworkElement)window.Content, "media-column-fill");

        grid.Columns[0].Width = 2000;
        await LayoutAsync(window);
        Assert.AreEqual(255, grid.Columns[^1].Width, 0.1, "Keep actions usable when there is insufficient space.");
        Assert.IsTrue(scroll.ScrollableWidth > 0, "Narrow layouts must remain horizontally scrollable.");

        grid.Columns[0].Width = originalColumnWidth;
        list.Padding = originalPadding;
        scroll.VerticalScrollBarVisibility = originalScrollVisibility;
        window.Width = originalWindowWidth;
        await LayoutAsync(window);
    }

    private static DragDropEffects RaiseFileDrag(UIElement target, IDataObject data, RoutedEvent routedEvent, DragDropEffects allowed = DragDropEffects.Copy)
    {
        // WPF constructs these internally for native drops; raise the same routed events in-process.
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, [data, DragDropKeyStates.None, allowed, target, new Point()], null)!;
        args.RoutedEvent = routedEvent;
        target.RaiseEvent(args);
        Assert.IsTrue(args.Handled);
        return args.Effects;
    }

    private static async Task VerifyFileDropAsync(TestRuntime runtime, MediaViewModel media, Window window, DependencyObject mediaView)
    {
        var root = Descendants<Grid>(mediaView).Single(grid => FileDrop.GetCommand(grid) is not null);
        var panel = Descendants<StackPanel>(mediaView).Single(element => element.Name == "EmptyMediaPanel");
        Assert.IsTrue(root.AllowDrop);
        Assert.IsNotNull(root.Background, "Blank page space must participate in hit testing.");
        Assert.AreSame(media.AddDroppedPathsCommand, FileDrop.GetCommand(root));
        Assert.IsTrue(Descendants<TextBlock>(panel).Any(text => text.Text.Contains("Drag and drop")));
        string[] paths = [@"C:\Dropped\Mountain.mkv", @"C:\Dropped\Folder"];
        runtime.Folders.Add(paths[1]);
        var data = new DataObject(DataFormats.FileDrop, paths.Append(@"C:\Dropped\Movie.mp4").ToArray());
        var unsupported = new DataObject(DataFormats.FileDrop, new[] { @"C:\Dropped\Movie.mp4" });
        Assert.AreEqual(DragDropEffects.None, RaiseFileDrag(root, unsupported, UIElement.PreviewDragEnterEvent));
        Assert.AreEqual(DragDropEffects.None, RaiseFileDrag(root, unsupported, UIElement.PreviewDragOverEvent));
        Assert.AreEqual(DragDropEffects.None, RaiseFileDrag(root, unsupported, UIElement.PreviewDropEvent));
        Assert.AreEqual(DragDropEffects.None, RaiseFileDrag(root, new DataObject(DataFormats.Text, "text"), UIElement.PreviewDropEvent));
        Assert.AreEqual(DragDropEffects.None, RaiseFileDrag(root, new DataObject(DataFormats.FileDrop, Array.Empty<string>()), UIElement.PreviewDropEvent));
        Assert.AreEqual(DragDropEffects.None, RaiseFileDrag(root, data, UIElement.PreviewDropEvent, DragDropEffects.Move));
        Assert.IsEmpty(media.Files);
        Assert.AreEqual(DragDropEffects.Copy, RaiseFileDrag(panel, data, UIElement.PreviewDragEnterEvent));
        Assert.AreEqual(DragDropEffects.Copy, RaiseFileDrag(panel, data, UIElement.PreviewDragOverEvent));
        Assert.IsEmpty(media.Files, "Hovering must not add files.");

        var discovered = new List<string>();
        runtime.DiscoverFiles = input =>
        {
            discovered.Add(input);
            return input == paths[1] ? [paths[0], @"C:\Dropped\Ocean.mkv"] : [input];
        };
        int analyses = runtime.Analyses;
        try
        {
            Assert.AreEqual(DragDropEffects.Copy, RaiseFileDrag(root, data, UIElement.PreviewDropEvent));
            await media.Completion;
            await LayoutAsync(window);
            CollectionAssert.AreEqual(paths, discovered.ToArray());
            Assert.HasCount(2, media.Files, "Overlapping files and folders must be deduplicated.");
            Assert.AreEqual(analyses + 2, runtime.Analyses, "Dropped files use automatic scanning.");
            Assert.IsFalse(panel.IsVisible);

            var list = Descendants<ListView>(mediaView).Single();
            Assert.AreEqual(DragDropEffects.Copy, RaiseFileDrag(list, new DataObject(DataFormats.FileDrop, new[] { @"C:\Dropped\City.mkv" }), UIElement.PreviewDropEvent));
            await media.Completion;
            Assert.HasCount(3, media.Files, "Dropping on a populated list must still work.");
            await LayoutAsync(window);

            runtime.DiscoverFiles = _ => throw new IOException("Unavailable");
            Assert.AreEqual(DragDropEffects.Copy, RaiseFileDrag(list, data, UIElement.PreviewDropEvent));
            await media.Completion;
            Assert.AreEqual(ViewStatus.Error, media.Status);
            Assert.HasCount(3, media.Files);
        }
        finally
        {
            runtime.DiscoverFiles = null;
            media.ClearAllCommand.Execute();
            await LayoutAsync(window);
        }
    }

    private static async Task VerifyEmptyMediaAsync(TestRuntime runtime, MediaViewModel media, Window window, ListBox navigation, DependencyObject mediaView)
    {
        var panel = Descendants<StackPanel>(mediaView).Single(element => element.Name == "EmptyMediaPanel");
        TextBlock Status() => StatusItem(window, 1);
        TextBlock Selection() => StatusItem(window, 0);
        var addFiles = Button(panel, "Add files");
        var addFolder = Button(panel, "Add folder");
        Assert.IsTrue(panel.IsVisible);
        Assert.IsTrue(Status().IsVisible);
        Assert.AreEqual("0 items", Selection().Text);
        Assert.IsTrue(navigation.IsVisible && navigation.IsEnabled);
        Assert.AreSame(media.AddFilesCommand, addFiles.Command);
        Assert.AreSame(media.AddFolderCommand, addFolder.Command);
        Assert.IsTrue(addFiles.IsEnabled && addFolder.IsEnabled);
        Assert.IsTrue(addFiles.Focus());
        Assert.IsTrue(addFolder.Focus());
        CollectionAssert.AreEquivalent(new[] { addFiles, addFolder }, Descendants<Button>(mediaView).Where(button => button.IsVisible).ToArray());

        // Every status, including errors matching neutral messages, remains visible in the shell.
        foreach (string message in new[] { "Ready", "File list cleared." })
        {
            media.SetStatus(ViewStatus.Error, message);
            await LayoutAsync(window);
            Assert.IsTrue(Status().IsVisible);
            Assert.AreEqual(message, Status().Text);
        }
        media.SetStatus(ViewStatus.Cancelled);
        await LayoutAsync(window);
        Assert.IsTrue(Status().IsVisible);
        Assert.AreEqual("Cancelled; cleanup finished.", Status().Text);
        media.SetStatus(ViewStatus.Ready);
        await LayoutAsync(window);
        Assert.IsTrue(Status().IsVisible);
        Assert.AreEqual("Ready", Status().Text);

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

        string longError = string.Join(" ", Enumerable.Repeat("Long operation error with file details.", 20));
        media.SetStatus(ViewStatus.Error, longError);
        await LayoutAsync(window);
        Assert.AreEqual(longError, Status().Text);
        Assert.AreEqual(longError, Status().ToolTip);
        Assert.AreEqual(TextTrimming.CharacterEllipsis, Status().TextTrimming);
        AssertInside(Status(), content);

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
        Assert.IsTrue(Status().IsVisible);
        Assert.AreEqual(@"Could not add C:\Media: Folder unavailable", Status().Text);
        AssertInside(Status(), content);
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
            Assert.IsFalse(media.AddDroppedPathsCommand.CanExecute(new[] { @"C:\Media\Dropped.mkv" }));
            Assert.AreEqual(DragDropEffects.None, RaiseFileDrag(panel, new DataObject(DataFormats.FileDrop, new[] { @"C:\Media\Dropped.mkv" }), UIElement.PreviewDropEvent));
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
        Assert.IsTrue(Selection().IsVisible);
        Assert.AreEqual(media.SelectionSummary, Selection().Text);
        media.SetStatus(ViewStatus.Error, "Media operation failed.");
        await LayoutAsync(window);
        Assert.IsTrue(Status().IsVisible);
        Assert.AreEqual(media.StatusMessage, Status().Text, "Populated and empty lists must use the same status element.");
        AssertInside(Status(), content);
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
        Assert.IsTrue(Status().IsVisible);
        Assert.AreEqual(ViewStatus.FileListCleared, media.Status);
        Assert.AreEqual("File list cleared.", Status().Text);
        Assert.AreEqual("0 items", Selection().Text);
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
        var remove = Descendants<Button>(container).Single(button => Equals(button.ToolTip, "Remove"));
        var convert = Descendants<Button>(container).Single(button => Equals(button.ToolTip, "Convert to DV8.1"));
        var restore = Descendants<Button>(container).Single(button => Equals(button.ToolTip, "Restore to DV7 from .dovi archive"));
        Assert.AreSame(media.RestoreRowCommand, restore.Command);
        Assert.AreSame(row, restore.CommandParameter);
        Assert.AreEqual("\uE7A7", restore.Content);
        Assert.AreEqual(Visibility.Collapsed, restore.Visibility);
        Assert.AreSame(media.ConvertRowDv81Command, convert.Command);
        Assert.AreSame(row, convert.CommandParameter);
        Assert.AreEqual("Convert to DV8.1", System.Windows.Automation.AutomationProperties.GetName(convert));
        Assert.AreEqual("\uE8AB", convert.Content);
        var actions = (StackPanel)remove.Parent;
        Assert.AreSame(convert, actions.Children[actions.Children.IndexOf(remove) - 1]);
        Assert.AreSame(restore, actions.Children[actions.Children.IndexOf(convert) - 1]);
        Assert.AreEqual(Visibility.Hidden, convert.Visibility);
        var originalAnalysis = row.Analysis!;
        row.Analysis = originalAnalysis with { Media = originalAnalysis.Media with { Profile = DolbyVisionProfile.Profile81 } };
        await LayoutAsync(window);
        Assert.AreEqual(Visibility.Collapsed, convert.Visibility);
        Assert.AreEqual(Visibility.Collapsed, restore.Visibility);
        row.RestoreArchive = Path.ChangeExtension(row.Path, ".dovi");
        await LayoutAsync(window);
        Assert.AreEqual(Visibility.Hidden, restore.Visibility);
        Assert.IsTrue(restore.IsEnabled);
        row.Analysis = null;
        await LayoutAsync(window);
        Assert.AreEqual(Visibility.Collapsed, convert.Visibility);
        Assert.AreEqual(Visibility.Collapsed, restore.Visibility);
        row.Analysis = originalAnalysis;
        await LayoutAsync(window);
        Assert.AreEqual(Visibility.Hidden, convert.Visibility);
        Assert.AreEqual(Visibility.Collapsed, restore.Visibility);
        row.RestoreArchive = null;
        Assert.AreSame(media.RemoveFileCommand, remove.Command);
        Assert.AreSame(row, remove.CommandParameter);
        Assert.AreEqual("Remove from list", System.Windows.Automation.AutomationProperties.GetName(remove));
        Assert.AreEqual("\uE74D", remove.Content);
        Assert.IsFalse(remove.IsVisible);
        container.IsSelected = true;
        await LayoutAsync(window);
        Assert.IsFalse(remove.IsVisible, "Selecting a row without hovering must keep hover actions hidden.");
        Assert.IsFalse(convert.IsVisible);
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
            await LayoutAsync(window);
            var status = StatusItem(window, 0);
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

    private static TextBlock StatusItem(DependencyObject window, int index) =>
        Descendants<TextBlock>(Descendants<StatusBar>(window).Single()).ElementAt(index);

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

    private static void VerifyCommandIcons(DependencyObject root)
    {
        var buttons = Descendants<Button>(root).Where(button => button.IsVisible && button.Tag is string && button.ContentTemplate is not null).ToArray();
        Assert.IsNotEmpty(buttons);
        foreach (var button in buttons)
        {
            var glyph = (string)button.Tag;
            var icon = Descendants<TextBlock>(button).Single(text => text.Text == glyph);
            Assert.AreEqual(button.FindResource("SymbolThemeFontFamily"), icon.FontFamily);
            Assert.IsTrue(new Typeface(icon.FontFamily, icon.FontStyle, icon.FontWeight, icon.FontStretch).TryGetGlyphTypeface(out var font));
            Assert.IsTrue(font.CharacterToGlyphMap.ContainsKey(glyph[0]), $"Missing icon for {button.Content}.");
            Assert.AreEqual(button.Content, new ButtonAutomationPeer(button).GetName());
            AssertInside(icon, button);
            if (button.Content is "Add files" or "Add folder")
            {
                Assert.AreEqual(button.Content is "Add files" ? "\uE8A5" : "\uE8B7", glyph);
                var badge = Descendants<TextBlock>(button).Single(text => text.Text == "\uE710");
                Assert.AreEqual(icon.FontFamily, badge.FontFamily);
                AssertInside(badge, button);
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
