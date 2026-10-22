using System.Runtime.InteropServices;
using System.Windows;
using WpfFoundation.Dialogs;

namespace DoViFixer.App.Dialogs;

/// <summary>Puts text on the Windows clipboard.</summary>
public interface ITextClipboard
{
    void SetText(string text);
}

public sealed class TextClipboard(IDialogService dialogs) : ITextClipboard
{
    public void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException ex)
        {
            // Another application can hold the clipboard open. The dialog is modal, so the returned task has completed when it closes.
            _ = dialogs.ShowMessageAsync("Unable to copy", $"Another application is using the clipboard. Try again.\n\n{ex.Message}");
        }
    }
}
