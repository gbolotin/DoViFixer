using System.ComponentModel;
using System.Diagnostics;
using WpfFoundation.Dialogs;

namespace DoViFixer.App.Dialogs;

/// <summary>Opens web pages, such as the online documentation, in the user's default browser.</summary>
public interface IWebBrowser
{
    void Open(Uri address);
}

public sealed class DefaultBrowser(IDialogService dialogs) : IWebBrowser
{
    /// <summary>The online documentation that Help › Documentation (F1) opens.</summary>
    public static Uri DocumentationAddress { get; } = new("https://gbolotin.github.io/DoViFixer/");

    public void Open(Uri address)
    {
        try
        {
            // Shell execution hands the address to the default browser.
            using var process = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // The dialog is modal, so the returned task has completed when it closes.
            _ = dialogs.ShowMessageAsync("Unable to open the page", $"Open {address.AbsoluteUri} in your browser.\n\n{ex.Message}");
        }
    }
}
