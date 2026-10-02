using DoViFixer.Application.Dependencies;

namespace DoViFixer.App.Presentation.Application;
public sealed class DependencyReportViewModel : ObservableObject
{
    private DependencyReport? report;
    private string? checkError;

    public IReadOnlyList<DependencyStatus> Tools => report?.Tools ?? [];
    public IReadOnlyList<NativeToolRow> ToolRows => Tools.Select(tool => new NativeToolRow(tool)).ToArray();
    public bool HasWarning => checkError is not null || report is { Ready: false };
    public string WarningText => checkError is not null ? "Dependency check failed" : "Dependencies missing or unavailable";
    public string WarningDetails => (checkError ?? string.Join("\n", Tools.Where(t => t.State != DependencyState.Ready).Select(t => $"{t.Tool}: {t.State} — {t.Diagnostic}"))) + "\nOpen Settings → Dependency setup to install or configure tools.";

    public void Update(DependencyReport value)
    {
        report = value;
        checkError = null;
        OnPropertyChanged(nameof(Tools));
        OnPropertyChanged(nameof(ToolRows));
        NotifyWarning();
    }

    public void Fail(string error)
    {
        checkError = error;
        NotifyWarning();
    }

    private void NotifyWarning()
    {
        OnPropertyChanged(nameof(WarningText));
        OnPropertyChanged(nameof(WarningDetails));
        OnPropertyChanged(nameof(HasWarning));
    }
}

/// <summary>One native tool as listed in Settings: what it is for and its detected state.</summary>
public sealed record NativeToolRow(DependencyStatus Status)
{
    public string Name => NativeToolDescriptions.Name(Status.Tool);
    public string Purpose => NativeToolDescriptions.Purpose(Status.Tool);
    public string StateText => string.IsNullOrWhiteSpace(Status.Version) ? Status.State.ToString() : $"{Status.State} · {Status.Version}";
    public bool IsReady => Status.State == DependencyState.Ready;
}
