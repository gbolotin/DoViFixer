using System.Collections.Immutable;
using System.ComponentModel;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Settings;
using Microsoft.Extensions.Logging;
using WpfFoundation.Behaviors;

namespace DoViFixer.App.ViewModels;

public sealed partial class MediaViewModel
{
    private IReadOnlyList<GridViewColumnLayout>? columnLayout;
    private bool columnLayoutLoaded;
    private bool restoringColumnLayout;
    private int columnLayoutVersion;
    private Task columnLayoutSave = Task.CompletedTask;

    public IReadOnlyList<GridViewColumnLayout>? ColumnLayout
    {
        get => columnLayout;
        set
        {
            if (SetProperty(ref columnLayout, value))
            {
                QueueColumnLayoutSave();
            }
        }
    }

    private void LoadColumnLayout(UserSettings snapshot)
    {
        if (columnLayoutLoaded)
        {
            return;
        }

        var saved = snapshot.MediaListLayout;
        restoringColumnLayout = true;
        try
        {
            if (saved is not null)
            {
                ColumnLayout = saved.Columns.IsDefault ? null : saved.Columns
                    .Where(column => column is not null && !string.IsNullOrWhiteSpace(column.Id))
                    .Select(column => new GridViewColumnLayout(column.Id, column.Width)).ToArray();
                if (Enum.TryParse<MediaSortColumn>(saved.SortColumn, out var sortColumn) && Enum.IsDefined(sortColumn))
                {
                    FileSort.Restore(sortColumn, saved.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending);
                }
            }
        }
        finally
        {
            restoringColumnLayout = false;
            columnLayoutLoaded = true;
        }
    }

    private void OnFileSortChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileSort.Column))
        {
            QueueColumnLayoutSave();
        }
    }

    private void QueueColumnLayoutSave()
    {
        if (!columnLayoutLoaded || restoringColumnLayout)
        {
            return;
        }

        var snapshot = new MediaListLayout(ColumnLayout?.Select(column => new MediaColumnLayout(column.Id, column.Width)).ToImmutableArray() ?? [],
            FileSort.Column?.ToString(), FileSort.Direction == ListSortDirection.Descending);
        int version = ++columnLayoutVersion;
        columnLayoutSave = SaveColumnLayoutAsync(columnLayoutSave, snapshot, version);
    }

    private async Task SaveColumnLayoutAsync(Task previous, MediaListLayout snapshot, int version)
    {
        await previous;
        if (version != columnLayoutVersion)
        {
            return;
        }

        await Task.Delay(150);
        if (version != columnLayoutVersion)
        {
            return;
        }

        try
        {
            await settings.SetMediaListLayoutAsync(snapshot, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not save media column layout");
            if (IsIdle)
            {
                SetStatus(ViewStatus.Error, "Could not save the column layout. " + ex.Message);
            }
        }
    }

    public Task FlushColumnLayoutAsync() => columnLayoutSave;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ResetColumnLayout()
    {
        restoringColumnLayout = true;
        try
        {
            ColumnLayout = null;
            FileSort.Restore(null);
        }
        finally
        {
            restoringColumnLayout = false;
        }
        QueueColumnLayoutSave();
    }
}
