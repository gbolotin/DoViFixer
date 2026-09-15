using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace DoViFixer.Mockup.ViewModels;
public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class MockFile(string name, string analysis, string size, string duration, bool included) : ObservableModel
{
    private bool isIncluded = included;
    public string Name
    {
        get;
    }
    = name;
    public string Analysis
    {
        get;
    }
    = analysis;
    public string Size
    {
        get;
    }
    = size;
    public string Duration
    {
        get;
    }
    = duration;
    public string Profile => "Profile 7";
    public string Resolution => "3840 × 2160";
    public string OutputName => Name.Replace(".mkv", ".P8.1.mkv", StringComparison.Ordinal);
    public string InputPath => @"D:\Movies\" + Name;
    public string OutputPath => @"D:\Movies\Converted\" + OutputName;
    public string PictureNote => Analysis == "MEL" ? "Base layer and RPU metadata retained." : "Enhancement-layer picture data will be lost. Base layer and RPU metadata retained.";

    public bool IsIncluded
    {
        get => isIncluded;
        set
        {
            isIncluded = value;
            Notify();
        }
    }
}

public sealed class MockCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class MockWorkspace : ObservableModel, IDisposable
{
    private readonly DispatcherTimer timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(180)
    };
    private MockFile selectedFile;
    private string status = "Ready to review • sample library loaded";
    private string stage = "Ready";
    private double progress;
    private bool isRunning;
    private bool keepOriginal = true;
    public ObservableCollection<MockFile> Files
    {
        get;
    }
    = [new("Mountain.mkv", "Simple FEL", "52.3 GB", "01:48:27", true), new("Ocean.mkv", "MEL", "46.1 GB", "01:36:12", true), new("City.mkv", "Complex FEL", "68.7 GB", "02:15:03", false)];

    public MockWorkspace()
    {
        selectedFile = Files[0];
        ReviewCommand = new(() => Status = "Sample plan ready • inspect output paths and retention before simulating.", () => !isRunning && Files.Any(f => f.IsIncluded));
        StartCommand = new(Start, () => !isRunning && Files.Any(f => f.IsIncluded));
        CancelCommand = new(Cancel, () => isRunning);
        ResetCommand = new(Reset);
        ScanCommand = new(() => Status = "Sample scan complete • 3 Profile 7 files • no disk access");
        InspectCommand = new(() => Status = $"{SelectedFile.Name}: illustrative full-frame analysis. Brightness evidence does not guarantee playback compatibility.");
        foreach (var file in Files)
        {
            file.PropertyChanged += OnFileChanged;
        }

        timer.Tick += OnTick;
    }

    public MockFile SelectedFile
    {
        get => selectedFile;
        set
        {
            if (value is not null)
            {
                selectedFile = value;
                Notify();
            }
        }
    }

    public bool KeepOriginal
    {
        get => keepOriginal;
        set
        {
            keepOriginal = value;
            Notify();
            Notify(nameof(Retention));
        }
    }

    public string Retention => KeepOriginal ? "Keep original file" : "Remove only after verified success (simulation)";
    public string SelectionSummary => $"{Files.Count(f => f.IsIncluded)} selected · {Files.Count(f => !f.IsIncluded)} excluded";

    public string Status
    {
        get => status;
        private set
        {
            status = value;
            Notify();
        }
    }

    public string Stage
    {
        get => stage;
        private set
        {
            stage = value;
            Notify();
        }
    }

    public double Progress
    {
        get => progress;
        private set
        {
            progress = value;
            Notify();
        }
    }

    public MockCommand ReviewCommand
    {
        get;
    }
    public MockCommand StartCommand
    {
        get;
    }
    public MockCommand CancelCommand
    {
        get;
    }
    public MockCommand ResetCommand
    {
        get;
    }
    public MockCommand ScanCommand
    {
        get;
    }
    public MockCommand InspectCommand
    {
        get;
    }

    private void OnFileChanged(object? sender, PropertyChangedEventArgs e)
    {
        Notify(nameof(SelectionSummary));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        ReviewCommand.Refresh();
        StartCommand.Refresh();
        CancelCommand.Refresh();
    }

    private void Start()
    {
        isRunning = true;
        Progress = 0;
        Stage = "Extracting";
        Status = "Simulation running • no real files or tools are used";
        RefreshCommands();
        timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        Progress = Math.Min(100, Progress + 2);
        Stage = Progress switch
        {
            < 25 => "Extracting",
            < 50 => "Converting metadata",
            < 75 => "Remuxing",
            < 100 => "Verifying output",
            _ => "Complete"
        };
        if (Progress >= 100)
        {
            timer.Stop();
            isRunning = false;
            Status = "Simulation complete • no files were created, changed, or deleted";
            RefreshCommands();
        }
    }

    private void Cancel()
    {
        timer.Stop();
        isRunning = false;
        Stage = "Cancelled";
        Status = "Simulation cancelled • original files untouched";
        RefreshCommands();
    }

    private void Reset()
    {
        timer.Stop();
        isRunning = false;
        Progress = 0;
        Stage = "Ready";
        KeepOriginal = true;
        for (var i = 0; i < Files.Count; i++)
        {
            Files[i].IsIncluded = i < 2;
        }

        SelectedFile = Files[0];
        Status = "Ready to review • sample library loaded";
        RefreshCommands();
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= OnTick;
        foreach (var file in Files)
        {
            file.PropertyChanged -= OnFileChanged;
        }
    }
}
