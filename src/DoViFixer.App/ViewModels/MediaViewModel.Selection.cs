using Microsoft.Extensions.Logging;

namespace DoViFixer.App.ViewModels;

/// <summary>
/// The two ways the list selects files. With check boxes, the checked rows are the selected files and clicking a row
/// only shows its details. Without them, the list selects rows as File Explorer does (click, Ctrl+click, Shift+click,
/// Ctrl+A), and the highlighted rows are the selected files. Either way <see cref="MediaRow.IsSelected"/> holds the
/// files that toolbar and menu actions process.
/// </summary>
public sealed partial class MediaViewModel
{
    private bool showsCheckBoxes;
    private bool applyingSettings;
    private IReadOnlyList<object> selectedRows = [];
    private bool updatingRowSelection;
    private bool listSelectionPending;
    private Task selectionModeSave = Task.CompletedTask;

    /// <summary>Whether each row has a check box. Changing it here, as the View menu does, saves the choice.</summary>
    public bool ShowsCheckBoxes
    {
        get => showsCheckBoxes;
        set
        {
            if (!SetProperty(ref showsCheckBoxes, value))
            {
                return;
            }

            // The list has switched its selection mode by now, so it can take the selected rows.
            PublishListSelection(replacePending: true);
            CommandsChanged();
            if (!applyingSettings)
            {
                selectionModeSave = SaveSelectionModeAsync(selectionModeSave, value);
            }
        }
    }

    /// <summary>
    /// The rows the list highlights when it selects as File Explorer does. While an operation runs, the highlight
    /// may change but the selected files do not until it ends, so selecting rows never skips a queued file.
    /// </summary>
    public IReadOnlyList<object> SelectedRows
    {
        get => selectedRows;
        set
        {
            if (!SetProperty(ref selectedRows, value ?? []))
            {
                return;
            }

            listSelectionPending = true;
            ApplyListSelection();
        }
    }

    private void ApplySelectionMode(bool showCheckBoxes)
    {
        applyingSettings = true;
        try
        {
            ShowsCheckBoxes = showCheckBoxes;
        }
        finally
        {
            applyingSettings = false;
        }
    }

    private void ApplyListSelection()
    {
        if (ShowsCheckBoxes || IsBusy || !listSelectionPending)
        {
            return;
        }

        listSelectionPending = false;
        var highlighted = selectedRows.OfType<MediaRow>().ToHashSet();
        SetRowSelection(ShownFiles.ToArray(), highlighted.Contains, publish: false);
    }

    /// <summary>
    /// Shows the selected files as the list's highlight. A highlight not applied yet is kept unless
    /// <paramref name="replacePending"/> is set, so rows changing during an operation do not undo the user's choice.
    /// </summary>
    private void PublishListSelection(bool replacePending = false)
    {
        if (ShowsCheckBoxes || updatingRowSelection || (listSelectionPending && !replacePending))
        {
            return;
        }

        listSelectionPending = false;
        selectedRows = [.. ShownFiles.Where(row => row.IsSelected)];
        OnPropertyChanged(nameof(SelectedRows));
    }

    /// <summary>Selects rows in one pass, so the list's highlight is updated once rather than once per row.</summary>
    private void SetRowSelection(IEnumerable<MediaRow> rows, Func<MediaRow, bool> selected, bool publish = true)
    {
        updatingRowSelection = true;
        try
        {
            foreach (var row in rows)
            {
                row.IsSelected = selected(row);
            }
        }
        finally
        {
            updatingRowSelection = false;
        }

        if (publish)
        {
            PublishListSelection(replacePending: true);
        }
    }

    private async Task SaveSelectionModeAsync(Task previous, bool showCheckBoxes)
    {
        await previous;
        try
        {
            await settings.SetShowSelectionCheckBoxesAsync(showCheckBoxes, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not save the media list selection mode");
            if (IsIdle)
            {
                SetStatus(ViewStatus.Error, "Could not save the list selection mode. " + ex.Message);
            }
        }
    }

    public Task FlushSelectionModeAsync() => selectionModeSave;

    private bool CanSelectAll() => ShowsCheckBoxes ? CanToggleSelectAll() : ShownFiles.Any();

    /// <summary>
    /// Ctrl+A with check boxes checks every file shown; without them it highlights every row shown. The list's key
    /// binding handles Ctrl+A even when this cannot run, so the list never gets to select all rows itself.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSelectAll))]
    private void SelectAll()
    {
        if (ShowsCheckBoxes)
        {
            SetRowSelection(ShownFiles.Where(row => row.SelectionEnabled), _ => true);
        }
        else
        {
            SelectedRows = [.. ShownFiles];
        }
    }

    private bool CanRemoveListSelection() => IsIdle && (ShowsCheckBoxes ? Focused is not null : ShownFiles.Any(row => row.IsSelected));

    /// <summary>The Delete key removes the highlighted rows from the list, or with check boxes the row with focus.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveListSelection))]
    private Task RemoveListSelection() => ShowsCheckBoxes ? RemoveFile(Focused) : RemoveSelected();
}
