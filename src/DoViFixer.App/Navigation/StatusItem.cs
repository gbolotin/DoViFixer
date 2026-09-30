using System.Windows.Input;

namespace DoViFixer.App.Navigation;

public sealed record StatusItem(string Text, string? ToolTip = null, bool IsEmphasized = false, ICommand? Command = null)
{
    public string ToolTipText => ToolTip ?? Text;
}
