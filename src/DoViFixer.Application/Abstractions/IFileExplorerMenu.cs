namespace DoViFixer.Application.Abstractions;

/// <summary>The commands File Explorer's right-click menu offers on Matroska files and folders to open or convert them in the app.</summary>
public interface IFileExplorerMenu
{
    /// <summary>
    /// Adds the commands, so they start <paramref name="executablePath"/>, when <paramref name="show"/> is
    /// <see langword="true"/>; otherwise removes them. Only the current user's menu changes, so no elevation is needed.
    /// </summary>
    void Update(bool show, string executablePath);
}
