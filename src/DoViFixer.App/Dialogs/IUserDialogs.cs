namespace DoViFixer.App.Dialogs;
public interface IUserDialogs
{
    string[] PickFiles(string filter = "Matroska media|*.mkv");
    string? PickFolder();
    void OpenFolder(string path);
    void ShowInFolder(string filePath);
    void OpenLogs();
    bool Review(string title, string content, string approveLabel);
    void ShowMessage(string message, string title = "DoViFixer");
}
