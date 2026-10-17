using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DoViFixer.App.ViewModels;
using DoViFixer.App.Presentation.Application;
using DoViFixer.App.Views;
using DoViFixer.Application.Dependencies;
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
        await DispatcherThread.RunAsync(ExerciseShellAsync, TimeSpan.FromSeconds(45));
    }

    private static async Task ExerciseShellAsync()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System };
        // The same dictionaries App.xaml merges.
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/WpfFoundation;component/Themes/WpfFoundation.xaml", UriKind.Absolute)
        });
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
            await shell.Settings.CheckCommand.InvokeAsync();
            await LayoutAsync(window);
            Assert.IsFalse(Descendants<StackPanel>(window).Any(panel => System.Windows.Automation.AutomationProperties.GetName(panel) == "Dependency warning"));
            window.Width = 1400;
            window.Height = 900;
            await LayoutAsync(window);
            var navigation = Descendants<ListBox>(window).Single(list => ReferenceEquals(list.ItemsSource, shell.Pages));
            var pageHost = Descendants<RetainedPageHost>(window).Single(host => ReferenceEquals(host.Navigation, shell.Navigation));
            var mediaView = PageView(pageHost, media);
            Assert.AreSame(media, navigation.SelectedItem);
            Assert.HasCount(2, navigation.Items);
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
            await VerifyFileFilterAsync(media, list, window);
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
            foreach (var page in shell.Pages.Skip(1).Append(media).Concat(shell.Pages.Skip(1).Append(media)))
            {
                navigation.SelectedItem = page;
                await shell.NavigationTask;
                await LayoutAsync(window);
                var workspace = PageView(pageHost, page);
                Assert.AreSame(page, shell.CurrentPage);
                var statusBar = Descendants<StatusBar>(window).Single();
                CollectionAssert.AreEqual(((OperationViewModel)page).StatusItems.Select(item => item.Text).ToArray(), Descendants<TextBlock>(statusBar).Select(text => text.Text).ToArray());
                AssertInside(statusBar, (FrameworkElement)window.Content);
                Assert.AreSame(page, workspace.Content);
                Assert.IsTrue(Descendants<FrameworkElement>(workspace).Any(element => ReferenceEquals(element.DataContext, page) && element.ActualHeight > 0));
                var root = VisualTreeHelper.GetChild(workspace, 0);
                if (retainedRoots.TryGetValue(page, out var previousRoot))
                {
                    Assert.AreSame(previousRoot, root, "Navigation must retain each page's visual tree.");
                }
                retainedRoots[page] = root;
                var containers = pageHost.RetainedPages.Select(item => PageView(pageHost, item)).ToArray();
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
            Assert.AreSame(media.Files, ((System.ComponentModel.ICollectionView)list.ItemsSource).SourceCollection, "The sorted list shows its own view of the files.");
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
                .Where(text => text.DataContext is MediaDetail && Grid.GetColumn(text) == 1 && text.IsVisible).ToArray();
            Assert.AreEqual(media.Focused!.DetailRows.Count, detailValues.Length);
            foreach (var value in detailValues)
            {
                Assert.AreEqual(TextAlignment.Right, value.TextAlignment);
                Assert.AreEqual(DisplayedText(value), value.ToolTip);
            }
            var fileLocation = detailValues.Single(text => text.Inlines.FirstInline is System.Windows.Documents.Hyperlink);
            Assert.AreEqual(media.Focused.Path, DisplayedText(fileLocation));
            var fileLocationLink = (System.Windows.Documents.Hyperlink)fileLocation.Inlines.FirstInline;
            Assert.AreSame(media.OpenFileLocationCommand, fileLocationLink.Command, "The file location link must open the folder.");
            fileLocationLink.Command.Invoke(fileLocationLink.CommandParameter);
            Assert.AreEqual(media.Focused.Path, runtime.ShownFile);
            SaveRender((FrameworkElement)window.Content, "command-icons-toolbar");
            await VerifyRowActionsAsync(runtime, media, list, window, navigation, mediaView);
            window.Width = 800;
            window.Height = 600;
            await LayoutAsync(window);
            var content = (FrameworkElement)window.Content;
            AssertInside(splitter, content);
            AssertInside(Descendants<Button>(mediaView).Single(button => Equals(button.ToolTip, "Rescan") && button.IsVisible), content);
            foreach (string caption in new[] { "Inspect", "Convert to DV8.1" })
            {
                AssertInside(Descendants<SplitButton>(mediaView).Single(button => Equals(button.Content, caption) && button.IsVisible), content);
            }
            // The buttons show what their menus last ran, and the wider captions still fit.
            media.IsDeepInspectionChosen = true;
            media.IsHdr10ConversionChosen = true;
            await LayoutAsync(window);
            foreach (string caption in new[] { "Deep Inspect", "Convert to HDR10" })
            {
                AssertInside(Descendants<SplitButton>(mediaView).Single(button => Equals(button.Content, caption) && button.IsVisible), content);
            }
            media.IsDeepInspectionChosen = false;
            media.IsHdr10ConversionChosen = false;
            await LayoutAsync(window);
            SaveRender(content, "media-minimum");
            var focusedBeforeComparison = media.Focused;
            var converted = media.Files.FirstOrDefault(row => row.ShowsResultComparison);
            if (converted is not null)
            {
                media.Focused = converted;
                await LayoutAsync(window);
                Assert.IsTrue(Descendants<TextBlock>(mediaView).Any(text => text.Text == "Result" && text.IsVisible), "Converted files compare the original with the result.");
                SaveRender(content, "media-details-comparison");
                media.Focused = focusedBeforeComparison;
                await LayoutAsync(window);
            }

            var mediaStatusBar = Descendants<StatusBar>(window).Single();
            foreach (string summary in new[] { media.OutputSummary, media.RetentionSummary, media.FelSummary, media.ArchiveSummary })
            {
                AssertInside(Descendants<TextBlock>(mediaStatusBar).Single(text => text.Text == summary), content);
                Assert.IsFalse(Descendants<TextBlock>(mediaView).Any(text => text.Text == summary));
            }

            Assert.IsFalse(Descendants<Button>(mediaView).Any(button => Equals(button.Content, "Settings")));
            await VerifyTotalAndInlineProgressAsync();
            await VerifySettingsFrameAsync(runtime, shell, window, navigation);
            await VerifySettingsRecoveryAsync(runtime, shell, window);
            Assert.AreEqual("", errors.Errors.ToString(), "WPF binding errors were reported.");
        }
        finally
        {
            source.Listeners.Remove(errors);
            source.Switch.Level = previousLevel;
            app.Shutdown();
        }
    }

    private static async Task VerifyTotalAndInlineProgressAsync()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { AllowFel = true, ConversionWorkers = 2 }, default);
        runtime.Preview = (_, _) => Task.FromResult(MediaPreviewTests.CreateImage());
        var window = runtime.Container.GetRequiredService<Shell>();
        var media = ((ShellViewModel)window.DataContext).Pages.OfType<MediaViewModel>().Single();
        await media.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        foreach (var row in media.Files)
        {
            row.IsSelected = true;
        }
        var started = System.Threading.Channels.Channel.CreateUnbounded<bool>();
        runtime.DuringConversion = async token =>
        {
            started.Writer.TryWrite(true);
            await Task.Delay(Timeout.Infinite, token);
        };
        window.Width = 1100;
        window.Height = 800;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.Show();
        await LayoutAsync(window);
        var view = Descendants<MediaView>(window).Single();
        var conversion = media.ConvertDv81Command.InvokeAsync();
        try
        {
            await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await LayoutAsync(window);
            Assert.AreEqual(2, media.BatchProgress.ActiveCount);
            Assert.AreEqual(1, Descendants<ProgressBar>(view).Count(bar => bar.IsVisible && System.Windows.Automation.AutomationProperties.GetName(bar) == "Overall progress"));
            Assert.IsFalse(Descendants<ProgressBar>(view).Any(bar => System.Windows.Automation.AutomationProperties.GetName(bar) == "Current job progress"));
            var list = Descendants<ListView>(view).Single();
            foreach (var row in media.Files)
            {
                var container = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(row);
                Assert.IsTrue(Descendants<ProgressBar>(container).Any(bar => bar.IsVisible && bar.Value == row.Progress.StagePercent));
            }
            SaveRender((FrameworkElement)window.Content, "total-and-inline-progress-minimum");
            var cancelled = media.Files[1];
            var item = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(cancelled);
            Invoke(Button(item, "Cancel"));
            for (int attempt = 0; media.BatchProgress.ActiveCount == 2 && attempt < 100; attempt++)
            {
                await Task.Delay(10);
            }
            Assert.AreEqual(1, media.BatchProgress.ActiveCount);
            await LayoutAsync(window);
            Assert.IsTrue(Descendants<ProgressBar>(view).Single(bar => System.Windows.Automation.AutomationProperties.GetName(bar) == "Overall progress").IsVisible);
            Assert.IsTrue(cancelled.IsCancellationRequested);
            Assert.IsFalse(media.Files[0].IsCancellationRequested);
        }
        finally
        {
            media.CancelCommand.Invoke();
            await conversion.WaitAsync(TimeSpan.FromSeconds(5));
            await media.CancelPreviewsAsync();
            window.Close();
        }
    }

    private static async Task VerifyClassificationColorsAsync(TestRuntime runtime, ListView list)
    {
        var row = new MediaRow(@"C:\Media\Classification.mkv");
        var text = (TextBlock)((GridView)list.View).Columns[1].CellTemplate.LoadContent();
        text.DataContext = row;
        var media = await runtime.ProbeAsync(row.Path, default);
        var evidence = new RpuEvidence(AnalysisMethod.FullRpu, EnhancementLayer.Mel, 1000, null, 1, 1);

        async Task AssertColorAsync(string brushKey)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(((SolidColorBrush)list.FindResource(brushKey)).Color, ((SolidColorBrush)text.Foreground).Color, brushKey);
        }

        await AssertColorAsync("TextFillColorSecondaryBrush");
        foreach (var (verdict, color) in new[]
        {
            (AnalysisVerdict.Mel, "SystemFillColorSuccessBrush"),
            (AnalysisVerdict.SimpleFel, "AccentTextFillColorPrimaryBrush"),
            (AnalysisVerdict.ComplexFel, "SystemFillColorCriticalBrush"),
            (AnalysisVerdict.AnalysisFailed, "SystemFillColorCriticalBrush"),
            (AnalysisVerdict.Unknown, "SystemFillColorCautionBrush"),
            (AnalysisVerdict.FelUnclassified, "SystemFillColorCautionBrush"),
            (AnalysisVerdict.NotApplicable, "TextFillColorSecondaryBrush")
        })
        {
            row.Analysis = new MediaAnalysis(media, evidence, verdict, "Test classification");
            await AssertColorAsync(color);
            row.AnalysisError = "";
            await AssertColorAsync("SystemFillColorCriticalBrush");
            row.AnalysisError = null;
            await AssertColorAsync(color);
        }
        row.Analysis = null;
        await AssertColorAsync("TextFillColorSecondaryBrush");
        row.AnalysisError = "Analysis failed";
        await AssertColorAsync("SystemFillColorCriticalBrush");
    }

    private static async Task VerifyHeaderSortingAsync(MediaViewModel media, ListView list, Window window)
    {
        var grid = (GridView)list.View;
        GridViewColumnHeader Header(int column) => Descendants<GridViewColumnHeader>(list).Single(header => ReferenceEquals(header.Column, grid.Columns[column]));
        TextBlock Indicator(GridViewColumnHeader header) => Descendants<TextBlock>(header).Single(text => text.Style == list.FindResource("WfColumnSortIndicatorStyle"));
        string ascending = char.ConvertFromUtf32(0xE70E);
        string descending = char.ConvertFromUtf32(0xE70D);
        void AssertIndicatorAboveCenter(GridViewColumnHeader header, string title)
        {
            // Like Windows Explorer: the indicator is centered over the column, above the title, inside the header.
            var indicator = Indicator(header);
            var label = Descendants<TextBlock>(header).Single(text => text.Text == title);
            var glyph = indicator.TransformToAncestor(header).TransformBounds(new Rect(indicator.RenderSize));
            var text = label.TransformToAncestor(header).TransformBounds(new Rect(label.RenderSize));
            string layout = $"indicator {glyph}, title {text}, header {header.RenderSize}";
            Assert.IsTrue(glyph.Top >= 0 && glyph.Bottom <= text.Top + 2, $"The indicator must sit above the title inside the header: {layout}.");
            Assert.AreEqual(header.ActualWidth / 2, (glyph.Left + glyph.Right) / 2, 4, $"The indicator must be centered over the column: {layout}.");
        }
        var fileHeader = Header(0);
        var profileHeader = Header(1);
        var selectAll = Descendants<CheckBox>(fileHeader).Single();
        var original = media.Files.ToArray();
        var focused = media.Focused;
        Assert.AreSame(list.FindResource("WfSortableColumnHeaderTemplate"), profileHeader.ContentTemplate, "Columns without a header template use the shared sortable header.");
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
        AssertIndicatorAboveCenter(fileHeader, "File");
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
        AssertIndicatorAboveCenter(profileHeader, "Profile / Type");

        // Leave the list sorted by name ascending, which matches the order the files were added.
        fileHeader.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, fileHeader));
        await LayoutAsync(window);
        CollectionAssert.AreEqual(original, list.Items.Cast<MediaRow>().ToArray());
    }

    private static async Task VerifyFileFilterAsync(MediaViewModel media, ListView list, Window window)
    {
        var chips = Descendants<RadioButton>(window).Where(button => button.DataContext is MediaFilterOption).ToArray();
        RadioButton Chip(MediaFileFilter filter) => chips.Single(chip => ((MediaFilterOption)chip.DataContext).Filter == filter);
        CollectionAssert.AreEqual(media.FilterOptions.Where(option => option.IsVisible).Select(option => option.Label).ToArray(),
            chips.Where(chip => chip.IsVisible).Select(chip => chip.Content).ToArray(), "Each visible filter choice is shown with its count.");
        Assert.IsTrue(Chip(MediaFileFilter.All).IsChecked);
        var focused = media.Focused;
        var sorted = list.Items.Cast<MediaRow>().ToArray();
        void AssertShows(string message) => CollectionAssert.AreEqual(sorted.Where(row => media.ShownFilePredicate(row)).ToArray(), list.Items.Cast<MediaRow>().ToArray(), message);
        TextBlock NoMatches() => Descendants<TextBlock>(window).Single(text => text.Text == "No files match this filter.");
        Assert.IsFalse(NoMatches().IsVisible);

        Chip(MediaFileFilter.Profile81).IsChecked = true;
        await LayoutAsync(window);
        Assert.AreEqual(MediaFileFilter.Profile81, media.FileFilter);
        Assert.IsFalse(Chip(MediaFileFilter.All).IsChecked, "Filter choices are exclusive.");
        Assert.IsEmpty(list.Items);
        Assert.IsTrue(NoMatches().IsVisible);

        Chip(MediaFileFilter.NotScanned).IsChecked = true;
        await LayoutAsync(window);
        AssertShows("The list shows only the chosen group, in the sorted order.");
        var row = sorted[0];
        string? error = row.AnalysisError;
        row.AnalysisError = "Fixture analysis error.";
        await LayoutAsync(window);
        CollectionAssert.Contains(list.Items, row, "A file whose analysis fails joins the unscanned group without a refresh.");
        AssertShows("Live filtering keeps the sorted order.");
        row.AnalysisError = error;
        await LayoutAsync(window);
        AssertShows("Clearing the error moves the file back to its group.");

        Chip(MediaFileFilter.All).IsChecked = true;
        await LayoutAsync(window);
        CollectionAssert.AreEqual(sorted, list.Items.Cast<MediaRow>().ToArray(), "Showing all files restores the full sorted list.");
        Assert.IsFalse(NoMatches().IsVisible);
        media.Focused = focused;
        await LayoutAsync(window);
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
            media.ClearAllCommand.Invoke();
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
        var discovering = media.AddFolderCommand.InvokeAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await LayoutAsync(window);
            Assert.IsTrue(panel.IsVisible);
            Assert.IsFalse(addFiles.IsEnabled || addFolder.IsEnabled);
            Assert.IsFalse(media.AddDroppedPathsCommand.CanExecute(new[] { @"C:\Media\Dropped.mkv" }));
            Assert.AreEqual(DragDropEffects.None, RaiseFileDrag(panel, new DataObject(DataFormats.FileDrop, new[] { @"C:\Media\Dropped.mkv" }), UIElement.PreviewDropEvent));
            Assert.IsTrue(Descendants<ProgressBar>(mediaView).Any(progress => progress.IsVisible));
            var cancel = NamedButton(mediaView, "Cancel batch");
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
        media.ClearAllCommand.Invoke();
        runtime.PickedFiles = [];
        runtime.PickedFolder = null;
        window.Width = 1400;
        window.Height = 900;
        await LayoutAsync(window);
    }

    private static async Task VerifyRowActionsAsync(TestRuntime runtime, MediaViewModel media, ListView list, Window window, ListBox navigation, DependencyObject mediaView)
    {
        await LayoutAsync(window);
        var slots = new Dictionary<string, Rect>();
        AssertStableActionSlots(list, slots, "initial rows");
        var row = media.Files[0];
        var container = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(row);
        var remove = Descendants<Button>(container).Single(button => Equals(button.ToolTip, "Remove"));
        var convert = Descendants<Button>(container).Single(button => Equals(button.ToolTip, "Convert to DV8.1"));
        var restore = Descendants<Button>(container).Single(button => Equals(button.ToolTip, "Restore to DV7 from .dovi archive"));
        Assert.AreSame(media.RestoreRowCommand, restore.Command);
        Assert.AreSame(row, restore.CommandParameter);
        Assert.AreEqual("\uE7A7", restore.Content);
        Assert.AreEqual(Visibility.Hidden, restore.Visibility);
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
        AssertStableActionSlots(list, slots, "Profile 8.1");
        Assert.AreEqual(Visibility.Hidden, convert.Visibility);
        Assert.AreEqual(Visibility.Hidden, restore.Visibility);
        row.RestoreArchive = Path.ChangeExtension(row.Path, ".dovi");
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "restore archive found");
        Assert.AreEqual(Visibility.Hidden, restore.Visibility);
        Assert.IsTrue(restore.IsEnabled);
        row.Analysis = null;
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "no analysis");
        Assert.AreEqual(Visibility.Hidden, convert.Visibility);
        Assert.AreEqual(Visibility.Hidden, restore.Visibility);
        row.Analysis = originalAnalysis;
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "Profile 7 again");
        Assert.AreEqual(Visibility.Hidden, convert.Visibility);
        Assert.AreEqual(Visibility.Hidden, restore.Visibility);
        row.RestoreArchive = null;
        Assert.AreSame(media.RemoveFileCommand, remove.Command);
        Assert.AreSame(row, remove.CommandParameter);
        Assert.AreEqual("Remove from list", System.Windows.Automation.AutomationProperties.GetName(remove));
        Assert.AreEqual("\uE74D", remove.Content);
        Assert.IsFalse(remove.IsVisible);
        container.IsSelected = true;
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "row selected");
        Assert.IsFalse(remove.IsVisible, "Selecting a row without hovering must keep hover actions hidden.");
        Assert.IsFalse(convert.IsVisible);
        container.IsSelected = false;
        media.Focused = media.Files[1];
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "other row focused");
        Assert.IsFalse(remove.IsVisible, "An unselected row without hover must hide delete.");
        foreach (var (caption, command) in new (string, object)[]
        {
            ("Cancel", media.CancelFileCommand), ("Retry", media.RetryAnalysisCommand),
            ("Inspect", media.InspectIncompleteCommand), ("Skip", media.SkipCommand)
        })
        {
            var button = Button(container, caption);
            Assert.AreSame(command, button.Command, caption + " must resolve through the actual page template.");
            Assert.AreSame(row, button.CommandParameter);
        }
        Assert.AreSame(media.OpenRowOutputCommand, ConvertedFileLink(container).Command, "The converted file link must resolve through the actual page template.");
        Assert.AreSame(row, ConvertedFileLink(container).CommandParameter);

        // A failed analysis must be retryable from the row even when another row is focused.
        await runtime.ClearAsync(default);
        runtime.DuringAnalysis = _ => throw new IOException("Analysis unavailable");
        await media.ScanCommand.InvokeAsync();
        runtime.DuringAnalysis = null;
        Assert.IsTrue(row.CanRetryAnalysis);
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "analysis failed");
        Invoke(Button(container, "Retry"));
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "retry started");
        await media.Completion;
        Assert.IsFalse(row.CanRetryAnalysis);
        Assert.IsTrue(media.Files[1].CanRetryAnalysis, "Retry must affect only the clicked row.");

        var analysis = row.Analysis!;
        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(analysis.Media, analysis.Evidence with { SuccessfulSamples = 9 });
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "incomplete scan");
        int fullAnalyses = runtime.FullAnalyses;
        Invoke(Button(container, "Inspect"));
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "inspected");
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
        var converting = media.ConvertDv81Command.InvokeAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await LayoutAsync(window);
            AssertStableActionSlots(list, slots, "conversion running");
            Assert.IsFalse(navigation.IsEnabled);
            Assert.IsFalse(convert.IsEnabled);
            Assert.AreEqual(Visibility.Hidden, convert.Visibility, "A disabled Profile 7 convert action keeps its space.");
            Assert.AreSame(row, media.BatchProgress.CurrentJob);
            Assert.AreEqual(45, row.Progress.StagePercent);
            var pending = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(media.Files[1]);
            Invoke(Button(pending, "Skip"));
            await LayoutAsync(window);
            AssertStableActionSlots(list, slots, "queued row skipped");
            Assert.AreEqual("Conversion skipped", media.Files[1].Status);
            Assert.IsFalse(media.Files[1].IsPending);
            var cancel = Button(container, "Cancel");
            Invoke(cancel);
            await recovering.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await LayoutAsync(window);
            AssertStableActionSlots(list, slots, "job cancelling");
            Assert.AreEqual("Cancelling…", row.Progress.Stage);
            Assert.IsFalse(cancel.IsEnabled);
            Assert.IsFalse(Descendants<Button>(mediaView).Any(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Cancel job"));
            SaveRender((FrameworkElement)window.Content, "cancelling-job");
        }
        finally
        {
            media.CancelCommand.Invoke();
            release.TrySetResult();
            await converting.WaitAsync(TimeSpan.FromSeconds(10));
            runtime.DuringConversion = null;
        }

        row.IsSelected = true;
        await media.ConvertDv81Command.InvokeAsync();
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "converted");
        var convertedFile = ConvertedFileLink(container);
        convertedFile.Command.Invoke(convertedFile.CommandParameter);
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "converted file shown");
        Assert.AreEqual(row.Result!.Output, runtime.ShownFile);
        var rowDot = ResultDot(list.ItemContainerGenerator.ContainerFromItem(row), row);
        Assert.IsTrue(rowDot.IsVisible, "A converted row shows a result dot before its status.");
        Assert.AreEqual(window.FindResource("SystemFillColorSuccessBrush"), rowDot.Fill);
        media.Focused = row;
        await LayoutAsync(window);
        var resultDot = ResultDot(mediaView, row.Result);
        Assert.IsTrue(resultDot.IsVisible, "The last operation result shows a dot before its status.");
        Assert.AreEqual(window.FindResource("SystemFillColorSuccessBrush"), resultDot.Fill);

        var removeMissing = Button(window, "Remove missing");
        Assert.AreEqual(Visibility.Collapsed, removeMissing.Visibility);
        var missing = media.Files[1];
        media.Focused = missing;
        runtime.MissingFiles.Add(missing.Path);
        runtime.RaiseSourceFilesChanged();
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "source file missing");
        var missingContainer = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(missing);
        var missingStatus = Descendants<TextBlock>(missingContainer).Single(text => text.Text == MediaRow.MissingStatus);
        Assert.AreEqual(window.FindResource("SystemFillColorCautionBrush"), missingStatus.Foreground);
        Assert.IsFalse(ResultDot(missingContainer, missing).IsVisible, "A missing file shows no result dot.");
        Assert.AreEqual(TextDecorationLocation.Strikethrough, Descendants<TextBlock>(missingContainer).Single(text => text.Text == missing.Name).TextDecorations.Single().Location);
        Assert.IsFalse(Descendants<CheckBox>(missingContainer).Single().IsEnabled);
        Assert.AreEqual(Visibility.Visible, removeMissing.Visibility);
        Assert.IsTrue(removeMissing.IsEnabled);
        AssertInside(removeMissing, (FrameworkElement)window.Content);
        SaveRender((FrameworkElement)window.Content, "missing-source-file");
        runtime.MissingFiles.Clear();
        runtime.RaiseSourceFilesChanged();
        await LayoutAsync(window);
        AssertStableActionSlots(list, slots, "source file restored");
        Assert.AreEqual(Visibility.Collapsed, removeMissing.Visibility);
    }

    /// <summary>
    /// The Settings button opens the frame over the page, every close gesture returns to the page, and each section
    /// renders the shared settings in its own retained view. Leaves the frame open on Conversion.
    /// </summary>
    private static async Task VerifySettingsFrameAsync(TestRuntime runtime, ShellViewModel shell, Window window, ListBox navigation)
    {
        var content = (FrameworkElement)window.Content;
        var overlay = Descendants<ContentOverlay>(window).Single();
        var open = Descendants<Button>(window).Single(button => ReferenceEquals(button.Command, shell.OpenSettingsCommand));
        Assert.AreEqual("Settings", open.Content);
        Assert.AreSame(window.FindResource("WfNavigationButtonStyle"), open.Style);
        var sidebar = (Border)((FrameworkElement)open.Parent).Parent;
        Assert.AreSame(window.FindResource("ListBoxBackground"), sidebar.Background, "The Settings button sits on the sidebar background.");
        Assert.AreEqual(Colors.Transparent, ((SolidColorBrush)navigation.Background).Color, "The pages show the sidebar background behind them, not a second layer.");
        var shortcut = window.InputBindings.OfType<KeyBinding>().Single(binding => ReferenceEquals(binding.Command, shell.OpenSettingsCommand));
        Assert.AreEqual(Key.OemComma, shortcut.Key);
        Assert.AreEqual(ModifierKeys.Control, shortcut.Modifiers, "Ctrl+comma opens Settings.");
        Assert.AreEqual("Settings (Ctrl+,)", open.ToolTip);
        Assert.AreEqual(Visibility.Collapsed, overlay.Visibility);
        AssertInside(open, content);
        runtime.AnalysisCacheBytes = 262144;
        runtime.FrameCacheBytes = 10485760;

        async Task OpenAsync()
        {
            Invoke(open);
            await LayoutAsync(window);
            await (shell.OpenSettingsCommand.ExecutionTask ?? Task.CompletedTask);
            await LayoutAsync(window);
            Assert.IsTrue(shell.IsSettingsOpen);
            Assert.IsTrue(overlay.IsVisible);
            Assert.IsFalse(open.IsEnabled || navigation.IsEnabled, "The pages behind the frame are inert.");
            if (window.IsActive)
            {
                Assert.IsTrue(OverlayPart(overlay, "PART_Card").IsKeyboardFocusWithin, "Opening moves focus into the frame.");
            }
        }

        foreach (var (gesture, close) in new (string, Action)[]
        {
            ("close button", () => Invoke(CloseButton(overlay))),
            ("click outside", () => OverlayPart(overlay, "PART_Smoke").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent })),
            ("Esc", () => PressEscape(Descendants<ListBox>(overlay).Single()))
        })
        {
            open.Focus();
            await OpenAsync();
            close();
            await LayoutAsync(window);
            await (shell.CloseSettingsCommand.ExecutionTask ?? Task.CompletedTask);
            await LayoutAsync(window);
            Assert.IsFalse(shell.IsSettingsOpen, gesture + " must close the frame.");
            Assert.AreEqual(Visibility.Collapsed, overlay.Visibility);
            Assert.IsTrue(open.IsEnabled && navigation.IsEnabled);
            if (window.IsActive)
            {
                Assert.IsTrue(open.IsKeyboardFocused, gesture + " must return focus to the Settings button.");
            }
        }

        await OpenAsync();
        var card = OverlayPart(overlay, "PART_Card");
        AssertInside(card, content);
        var title = Descendants<TextBlock>(card).Single(text => text.Text == "Settings");
        Assert.IsTrue(title.IsVisible, "The frame is titled Settings.");
        Assert.AreSame(window.FindResource("CaptionTextBlockStyle"), title.Style, "The title is a small label above the sections.");
        Assert.AreSame(window.FindResource("TextFillColorSecondaryBrush"), title.Foreground);
        Assert.AreSame(window.FindResource("ListBoxBackground"), overlay.HeaderBackground, "The title shares the section list's background.");
        var sections = Descendants<ListBox>(overlay).Single(list => ReferenceEquals(list.ItemsSource, shell.Settings.Sections.Pages));
        Assert.HasCount(7, sections.Items);
        var host = Descendants<RetainedPageHost>(overlay).Single();
        var retained = new Dictionary<object, DependencyObject>();
        double? toolsOffset = null;
        var pages = shell.Settings.Sections.Pages;
        foreach (var section in pages.Concat(pages))
        {
            sections.SelectedItem = section;
            await LayoutAsync(window);
            Assert.AreSame(section, shell.Settings.Sections.CurrentPage);
            var view = PageView(host, section);
            Assert.IsTrue(view.IsVisible);
            Assert.AreEqual(1, host.RetainedPages.Count(page => PageView(host, page).IsVisible), "Exactly one section is shown.");
            var root = (FrameworkElement)VisualTreeHelper.GetChild(view, 0);
            bool firstVisit = !retained.TryGetValue(section, out var previousRoot);
            if (!firstVisit)
            {
                Assert.AreSame(previousRoot, root, "Each section keeps its view.");
            }
            retained[section] = root;
            Assert.AreSame(shell.Settings, root.DataContext, "Every section binds to the shared settings.");
            Assert.IsTrue(Descendants<TextBlock>(view).Any(text => text.Text == section.NavigationName && text.IsVisible), "Each section is titled with its name.");
            AssertInside(view, content);
            var scroll = Descendants<ScrollViewer>(view).Single(viewer => viewer.Content is StackPanel);
            switch (section)
            {
                case GeneralSettingsSection when firstVisit:
                    SaveRender(content, "settings-minimum");
                    break;
                case AnalysisSettingsSection when firstVisit:
                    foreach (string name in new[] { "Clips per video", "Clip length", "Required scan coverage", "Peak-brightness percentile", "Brightness margin", "Assumed peak brightness" })
                    {
                        var input = Descendants<TextBox>(view).Single(box => System.Windows.Automation.AutomationProperties.GetName(box) == name);
                        input.BringIntoView();
                        await LayoutAsync(window);
                        AssertInside(input, content);
                        Assert.IsFalse(string.IsNullOrEmpty(WpfFoundation.Behaviors.TextBoxAssist.GetSuffix(input)));
                        Assert.IsFalse(string.IsNullOrEmpty(WpfFoundation.Behaviors.TextBoxAssist.GetPlaceholder(input)));
                        Assert.IsFalse(input.Text.Contains(WpfFoundation.Behaviors.TextBoxAssist.GetSuffix(input)!, StringComparison.Ordinal));
                    }
                    Assert.HasCount(2, Descendants<Slider>(view));
                    var marginInput = Descendants<TextBox>(view).Single(box => System.Windows.Automation.AutomationProperties.GetName(box) == "Brightness margin");
                    marginInput.BringIntoView();
                    await LayoutAsync(window);
                    string savedMargin = marginInput.Text;
                    marginInput.Text = "not a number";
                    await shell.Settings.SaveTask;
                    await LayoutAsync(window);
                    Assert.IsTrue(Validation.GetHasError(marginInput));
                    Assert.AreSame(window.FindResource("DefaultTextBoxInvalidationStyle"), Validation.GetErrorTemplate(marginInput));
                    Assert.IsFalse(Validation.GetHasError((DependencyObject)view), "The whole page must not receive field errors.");
                    StringAssert.Contains(UIElementAutomationPeer.CreatePeerForElement(marginInput).GetItemStatus(), "0 to 1000");
                    var validationLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(marginInput)!;
                    Assert.HasCount(1, validationLayer.GetAdorners(marginInput)!.Where(adorner => Descendants<Border>(adorner).Any()).ToArray());
                    var validationAdorner = validationLayer.GetAdorners(marginInput)!.Single(adorner => Descendants<Border>(adorner).Any());
                    await shell.Settings.Sections.NavigateAsync(shell.Settings.Sections.Pages.OfType<GeneralSettingsSection>().Single());
                    await LayoutAsync(window);
                    Assert.IsFalse(validationAdorner.IsVisible, "A validation outline must not leak onto another section.");
                    await shell.Settings.Sections.NavigateAsync(section);
                    await LayoutAsync(window);
                    Assert.HasCount(1, validationLayer.GetAdorners(marginInput)!.Where(adorner => Descendants<Border>(adorner).Any()).ToArray());
                    Assert.AreEqual("not a number", marginInput.Text);
                    marginInput.Text = savedMargin;
                    await shell.Settings.SaveTask;
                    await LayoutAsync(window);
                    Assert.IsFalse(Validation.GetHasError(marginInput));
                    Assert.IsFalse(validationLayer.GetAdorners(marginInput)?.Any(adorner => Descendants<Border>(adorner).Any()) ?? false);
                    Assert.AreEqual(string.Empty, UIElementAutomationPeer.CreatePeerForElement(marginInput).GetItemStatus());
                    SaveRender(content, "settings-analysis");
                    break;
                case PerformanceSettingsSection when firstVisit:
                    foreach (string name in new[] { "Simultaneous conversions", "Concurrent scan clips", "Tool process priority", "Video pipe buffer (KiB)" })
                    {
                        Assert.HasCount(1, Descendants<ComboBox>(view).Where(combo => System.Windows.Automation.AutomationProperties.GetName(combo) == name));
                    }
                    var hardwareSummary = Descendants<TextBlock>(view).Single(text => text.Text == shell.Settings.HardwareSummary);
                    var recommendation = Descendants<TextBlock>(view).Single(text => text.Text == shell.Settings.ConcurrencyRecommendation);
                    Assert.IsGreaterThanOrEqualTo(hardwareSummary.TranslatePoint(new Point(0, hardwareSummary.ActualHeight), content).Y,
                        recommendation.TranslatePoint(new Point(), content).Y, "Hardware recommendations must not overlap the hardware summary.");
                    SaveRender(content, "settings-performance");
                    var cacheSize = Descendants<TextBlock>(view).Single(text => text.Text == shell.Settings.CacheSizeText);
                    cacheSize.BringIntoView();
                    await LayoutAsync(window);
                    var cacheLocation = Descendants<TextBlock>(view).Single(text => text.Inlines.FirstInline is System.Windows.Documents.Run { Text: "Location: " });
                    var cacheLocationLink = cacheLocation.Inlines.OfType<System.Windows.Documents.Hyperlink>().Single();
                    Assert.AreEqual(shell.Settings.CacheDirectory, ((System.Windows.Documents.Run)cacheLocationLink.Inlines.FirstInline).Text);
                    Assert.AreSame(shell.Settings.OpenCacheFolderCommand, cacheLocationLink.Command, "The cache location link must open the cache folder.");
                    Assert.IsTrue(cacheSize.IsVisible);
                    AssertInside(cacheSize, content);
                    AssertInside(cacheLocation, content);
                    SaveRender(content, "settings-cache-size");
                    break;
                case NativeToolsSettingsSection when firstVisit:
                    foreach (var tool in Enum.GetValues<NativeTool>())
                    {
                        Assert.HasCount(1, Descendants<TextBlock>(view).Where(text => text.Text == NativeToolDescriptions.Purpose(tool)));
                    }
                    var toolDescriptions = Descendants<TextBlock>(view).Single(text => text.Text == NativeToolDescriptions.Purpose(NativeTool.DoviTool));
                    toolDescriptions.BringIntoView();
                    await LayoutAsync(window);
                    AssertInside(toolDescriptions, content);
                    SaveRender(content, "settings-tool-descriptions");
                    scroll.ScrollToEnd();
                    await LayoutAsync(window);
                    toolsOffset = scroll.VerticalOffset;
                    Assert.IsTrue(toolsOffset > 0, "The test must exercise a scrolled section.");
                    break;
                case NativeToolsSettingsSection:
                    Assert.AreEqual(toolsOffset!.Value, scroll.VerticalOffset, 0.1, "A section keeps its scroll position.");
                    break;
                case AboutSettingsSection:
                    Assert.HasCount(1, Descendants<TextBlock>(view).Where(text => text.Text == "DoViFixer " + shell.Settings.Version));
                    break;
            }
        }

        double width = window.Width;
        double height = window.Height;
        window.Width = 1400;
        window.Height = 900;
        sections.SelectedItem = pages[0];
        await LayoutAsync(window);
        AssertInside(card, content);
        Assert.AreEqual(1040, card.ActualWidth, 1, "A large window shows the frame at its largest width.");
        SaveRender(content, "settings-general");
        window.Width = width;
        window.Height = height;
        sections.SelectedItem = pages.OfType<ConversionSettingsSection>().Single();
        await LayoutAsync(window);
    }

    private static async Task VerifySettingsRecoveryAsync(TestRuntime runtime, ShellViewModel shell, Window window)
    {
        var content = (FrameworkElement)window.Content;
        var overlay = Descendants<ContentOverlay>(window).Single();
        var host = Descendants<RetainedPageHost>(overlay).Single();
        var sections = shell.Settings.Sections;
        var conversion = sections.Pages.OfType<ConversionSettingsSection>().Single();
        Assert.AreSame(conversion, sections.CurrentPage);
        var settingsView = PageView(host, conversion);
        var outputFolder = Descendants<ComboBox>(settingsView).Single(box => System.Windows.Automation.AutomationProperties.GetName(box) == "Output folder");
        var outputPath = Descendants<TextBox>(settingsView).Single(box => System.Windows.Automation.AutomationProperties.GetName(box) == "Output folder path");
        Assert.AreEqual(0, outputFolder.SelectedIndex);
        Assert.IsFalse(outputPath.IsVisible);
        outputFolder.SelectedIndex = 1;
        Assert.IsTrue(shell.Settings.OtherFolder);
        await shell.Settings.SaveTask;
        await LayoutAsync(window);
        Assert.IsTrue(outputPath.IsVisible);
        var discard = Button(overlay, "Discard unsaved changes");
        Assert.IsTrue(discard.IsVisible);
        AssertInside(discard, content);
        Assert.IsFalse(CloseButton(overlay).IsEnabled, "A save error keeps the frame open for recovery.");
        OverlayPart(overlay, "PART_Smoke").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        PressEscape(outputPath);
        await LayoutAsync(window);
        Assert.IsTrue(shell.IsSettingsOpen && overlay.IsVisible, "Neither a click outside nor Esc closes the frame with a save error.");
        await sections.NavigateAsync(sections.Pages.OfType<AboutSettingsSection>().Single());
        await LayoutAsync(window);
        Assert.IsTrue(discard.IsVisible, "Every section shows the recovery actions.");
        AssertInside(discard, content);
        SaveRender(content, "settings-save-error");
        await sections.NavigateAsync(conversion);
        await LayoutAsync(window);

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
        Assert.AreEqual(0, outputFolder.SelectedIndex);
        Assert.IsFalse(outputPath.IsVisible);

        runtime.DuringDependencyCheck = token => Task.Delay(Timeout.Infinite, token);
        var checking = shell.Settings.CheckCommand.InvokeAsync();
        try
        {
            await LayoutAsync(window);
            Assert.IsFalse(CloseButton(overlay).IsEnabled, "The frame stays open while a settings operation runs.");
            var cancel = Button(overlay, "Cancel operation");
            Assert.IsTrue(cancel.IsVisible);
            AssertInside(cancel, content);
            Invoke(cancel);
            await LayoutAsync(window);
            await checking.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(ViewStatus.Cancelled, shell.Settings.Status);
            await LayoutAsync(window);
            Assert.IsTrue(Descendants<TextBlock>(overlay).Any(text => text.Text == "Cancelled; cleanup finished." && text.IsVisible), "The frame shows the settings status.");
        }
        finally
        {
            shell.Settings.CancelCommand.Invoke();
            await checking.WaitAsync(TimeSpan.FromSeconds(10));
        }
        runtime.DuringDependencyCheck = null;
        Invoke(CloseButton(overlay));
        await LayoutAsync(window);
        await (shell.CloseSettingsCommand.ExecutionTask ?? Task.CompletedTask);
        await LayoutAsync(window);
        Assert.IsFalse(overlay.IsVisible);
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

    private static FrameworkElement OverlayPart(ContentOverlay overlay, string name) => (FrameworkElement)overlay.Template.FindName(name, overlay);

    private static Button CloseButton(ContentOverlay overlay) => NamedButton(overlay, "Close");

    private static void PressEscape(UIElement target) =>
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });

    private static Button NamedButton(DependencyObject parent, string name) => Descendants<Button>(parent).Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == name);

    private static string DisplayedText(TextBlock text) => new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text;

    private static TextBlock StatusItem(DependencyObject window, int index) =>
        Descendants<TextBlock>(Descendants<StatusBar>(window).Single()).ElementAt(index);

    /// <summary>
    /// Each row action occupies the same place in every row and state, so showing or hiding one action never
    /// moves another: hover actions always keep their space, and a row shows at most one primary action.
    /// </summary>
    private static void AssertStableActionSlots(ListView list, Dictionary<string, Rect> slots, string checkpoint)
    {
        string[] primaryActions = ["Cancel", "Skip", "Retry", "Inspect"];
        foreach (var item in list.Items)
        {
            var container = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(item);
            int shownPrimaryActions = ((TextBlock)ConvertedFileLink(container).Parent).Visibility == Visibility.Collapsed ? 0 : 1;
            foreach (var button in Descendants<Button>(container))
            {
                string name = System.Windows.Automation.AutomationProperties.GetName(button) is { Length: > 0 } automationName
                    ? automationName : (string)button.Content;
                bool primary = primaryActions.Contains(name);
                if (primary && button.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                shownPrimaryActions += primary ? 1 : 0;
                Assert.IsTrue(button.ActualWidth > 0, $"{name} must keep its space ({checkpoint}).");
                var bounds = button.TransformToAncestor(container).TransformBounds(new Rect(button.RenderSize));
                if (!slots.TryAdd(name, bounds))
                {
                    var expected = slots[name];
                    Assert.IsTrue(Math.Abs(expected.X - bounds.X) < 0.5 && Math.Abs(expected.Width - bounds.Width) < 0.5,
                        $"{name} moved from {expected} to {bounds} ({checkpoint}).");
                }
            }

            Assert.IsTrue(shownPrimaryActions <= 1, $"Primary actions share one slot ({checkpoint}).");
        }
    }

    private static System.Windows.Documents.Hyperlink ConvertedFileLink(DependencyObject row) => Descendants<TextBlock>(row)
        .SelectMany(text => text.Inlines.OfType<System.Windows.Documents.Hyperlink>())
        .Single(link => System.Windows.Automation.AutomationProperties.GetName(link) == "Show converted file");

    /// <summary>The result dot placed by the page template, not an ellipse inside a control's own template.</summary>
    private static System.Windows.Shapes.Ellipse ResultDot(DependencyObject parent, object? context)
    {
        var ellipses = Descendants<System.Windows.Shapes.Ellipse>(parent).ToArray();
        var dots = ellipses.Where(dot => dot.TemplatedParent is not Control && dot.DataContext == context).ToArray();
        Assert.AreEqual(1, dots.Length, "Result dots found: " + string.Join("; ", ellipses.Select(dot =>
            $"{dot.DataContext?.GetType().Name ?? "null"} in {dot.TemplatedParent?.GetType().Name ?? "no template"}, {dot.Width}x{dot.Height}")));
        return dots[0];
    }

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
        var buttons = Descendants<Button>(root).Where(button => button.IsVisible && button.Tag is string or Geometry && button.ContentTemplate is not null).ToArray();
        Assert.IsNotEmpty(buttons);
        foreach (var button in buttons)
        {
            Assert.AreEqual(button.Content, new ButtonAutomationPeer(button).GetName());
            if (button.Content is "Add files" or "Add folder")
            {
                // Library vector icons, so the "+" badge is part of the outline.
                Assert.AreSame(button.FindResource(button.Content is "Add files" ? "WfIconAddFiles" : "WfIconAddFolder"), button.Tag);
            }

            if (button.Tag is Geometry geometry)
            {
                var vector = Descendants<PathIcon>(button).Single();
                Assert.AreSame(geometry, vector.Data);
                AssertInside(vector, button);
                continue;
            }

            var glyph = (string)button.Tag;
            var icon = Descendants<TextBlock>(button).Single(text => text.Text == glyph);
            Assert.AreEqual(button.FindResource("SymbolThemeFontFamily"), icon.FontFamily);
            Assert.IsTrue(new Typeface(icon.FontFamily, icon.FontStyle, icon.FontWeight, icon.FontStretch).TryGetGlyphTypeface(out var font));
            Assert.IsTrue(font.CharacterToGlyphMap.ContainsKey(glyph[0]), $"Missing icon for {button.Content}.");
            AssertInside(icon, button);
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

    // The retained host keeps one view per visited page, shown or collapsed.
    private static ContentPresenter PageView(RetainedPageHost host, object page) =>
        ((Grid)VisualTreeHelper.GetChild(host, 0)).Children.OfType<ContentPresenter>().Single(view => ReferenceEquals(view.Content, page));

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
