namespace DoViFixer.App.Dialogs;
public interface IUserDialogs
{
    string[] PickFiles(string filter = "Matroska media|*.mkv");
    string? PickFolder();
    void OpenFolder(string path);
    void OpenLogs();
    bool Review(string title, string content, string approveLabel, string? requiredText = null);
}
