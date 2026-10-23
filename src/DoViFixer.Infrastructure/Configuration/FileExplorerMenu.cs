using DoViFixer.Application.Abstractions;
using DoViFixer.Application.FileExplorer;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Updates;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DoViFixer.Infrastructure.Configuration;

/// <summary>
/// Registers the right-click commands as static verbs under the current user's classes. Windows 11 shows them under
/// "Show more options"; its compact menu only lists commands of packaged apps.
/// </summary>
internal sealed class FileExplorerMenu(ILogger<FileExplorerMenu> logger, string classesKey = FileExplorerMenu.UserClassesKey) : IFileExplorerMenu
{
    internal const string UserClassesKey = @"Software\Classes";
    // Matroska files of any registered type, and folders, whose files the app finds as Add folder does.
    internal static readonly string[] Targets = [@"SystemFileAssociations\.mkv", "Directory"];
    internal const string OpenVerb = "DoViFixer.Open";
    internal const string ConvertVerb = "DoViFixer.Convert";

    public void Update(bool show, string executablePath)
    {
        using var classes = Registry.CurrentUser.CreateSubKey(classesKey);
        foreach (string target in Targets)
        {
            if (show)
            {
                string executable = Path.GetFullPath(executablePath);
                Write(classes, $@"{target}\shell\{OpenVerb}", $"Open in {ApplicationTitle.Name}", executable, $"\"{executable}\" \"%1\"");
                Write(classes, $@"{target}\shell\{ConvertVerb}", $"Convert with {ApplicationTitle.Name}", executable, $"\"{executable}\" {FileExplorerRequest.ConvertArgument} \"%1\"");
            }
            else
            {
                classes.DeleteSubKeyTree($@"{target}\shell\{OpenVerb}", throwOnMissingSubKey: false);
                classes.DeleteSubKeyTree($@"{target}\shell\{ConvertVerb}", throwOnMissingSubKey: false);
            }
        }

        OperationLog.Audit(logger, show ? "RegisterFileExplorerMenu" : "RemoveFileExplorerMenu", show ? executablePath : "File Explorer menu", "Completed");
    }

    private static void Write(RegistryKey classes, string verbKey, string label, string executable, string command)
    {
        using var verb = classes.CreateSubKey(verbKey);
        verb.SetValue("", label);
        verb.SetValue("Icon", $"\"{executable}\",0");
        // Explorer offers a verb for at most 15 selected items unless it handles them all; each starts the app, which
        // passes its file to the window that is already open.
        verb.SetValue("MultiSelectModel", "Player");
        using var commandKey = verb.CreateSubKey("command");
        commandKey.SetValue("", command);
    }
}
