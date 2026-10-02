using System.ComponentModel;
using System.Diagnostics;
using WpfFoundation.Dialogs;

namespace DoViFixer.App.Dialogs;

/// <summary>Opens folders and the log folder in File Explorer.</summary>
public interface IFileExplorer
{
    void OpenFolder(string path);
    void ShowInFolder(string filePath);
    void OpenLogs();
}

public sealed class FileExplorer(string logDirectory, IDialogService dialogs) : IFileExplorer
{
    public void OpenLogs() => OpenFolder(logDirectory);

    public void OpenFolder(string path)
    {
        StartExplorer(start => start.ArgumentList.Add(Path.GetFullPath(path)));
    }

    public void ShowInFolder(string filePath)
    {
        if (!File.Exists(filePath))
        {
            OpenFolder(Path.GetDirectoryName(filePath)!);
            return;
        }

        // Explorer parses "/select," itself, so the path is quoted here rather than through ArgumentList.
        StartExplorer(start => start.Arguments = $"/select,\"{Path.GetFullPath(filePath)}\"");
    }

    private void StartExplorer(Action<ProcessStartInfo> setArguments)
    {
        try
        {
            var start = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = false
            };
            setArguments(start);
            using var process = Process.Start(start);
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or ArgumentException)
        {
            // The dialog is modal, so the returned task has completed when it closes.
            _ = dialogs.ShowMessageAsync("Unable to open folder", ex.Message);
        }
    }
}
