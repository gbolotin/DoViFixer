using System.Collections.ObjectModel;
using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Abstractions;

namespace DoViFixer.App.ViewModels;
public sealed class SettingsViewModel : OperationViewModel
{
    private readonly SettingsService settings;
    private readonly IThemeService themeService;
    private string temporary = "";
    private string destination = "";
    private bool otherFolder;
    private bool replaceOriginal;
    private bool createArchive;
    private bool automaticallyScanAddedFiles = true;
    private bool useCachedResults = true;
    private bool autoSelectAfterScan = true;
    private NativeTool selectedTool;
    private string toolPath = "";
    private AppTheme selectedTheme = AppTheme.System;

    public SettingsViewModel(SettingsService settings, DependencyService dependencies, DependencySetup setup, IUserDialogs dialogs, IAnalysisCache cache, IThemeService themeService)
    {
        this.settings = settings;
        this.themeService = themeService;
        LoadCommand = new(() => RunAsync(async (token, _) =>
        {
            var value = await settings.ReadAsync(token);
            Temporary = value.TemporaryDirectory ?? "";
            Destination = value.OutputDirectory ?? "";
            OtherFolder = value.OutputDirectory is not null;
            ReplaceOriginal = value.ReplaceOriginal;
            CreateArchive = value.CreateElArchive;
            AutomaticallyScanAddedFiles = value.AutomaticallyScanAddedFiles;
            UseCachedResults = value.UseCachedResults;
            AutoSelectAfterScan = value.AutoSelectAfterScan;
            selectedTheme = value.Theme;
            RaisePropertyChanged(nameof(SelectedTheme));
            RaisePropertyChanged(nameof(IsSystemTheme));
            RaisePropertyChanged(nameof(IsLightTheme));
            RaisePropertyChanged(nameof(IsDarkTheme));
            themeService.ApplyTheme(value.Theme);
            Status = "Settings loaded.";
        }), () => IsIdle);
        BrowseTemporaryCommand = new(() => Temporary = dialogs.PickFolder() ?? Temporary);
        BrowseDestinationCommand = new(() => Destination = dialogs.PickFolder() ?? Destination);
        BrowseToolCommand = new(() => ToolPath = dialogs.PickFiles("Executables|*.exe").FirstOrDefault() ?? ToolPath);
        SaveCommand = new(() => RunAsync(async (token, _) =>
        {
            if (OtherFolder && string.IsNullOrWhiteSpace(Destination))
            {
                throw new InvalidOperationException("Choose an output folder.");
            }

            await Task.Run(() => settings.SetPreferencesAsync(Temporary, OtherFolder ? Destination : null, ReplaceOriginal, CreateArchive, token, AutomaticallyScanAddedFiles, UseCachedResults, AutoSelectAfterScan, SelectedTheme), token);
            Status = "Defaults saved. Each conversion still requires plan approval.";
        }), () => IsIdle);
        ClearCacheCommand = new(() => RunAsync(async (token, _) =>
        {
            int removed = await Task.Run(() => cache.ClearAsync(token), token);
            Status = $"Cleared {removed} cached analysis results. Future scans will analyze those files again.";
        }), () => IsIdle);
        CheckCommand = new(() => RunAsync(async (token, _) =>
        {
            var report = await dependencies.CheckAsync(DependencyRequirements.All, token);
            Tools.Clear();
            foreach (var tool in report.Tools)
            {
                Tools.Add(tool);
            }

            Status = report.Ready ? "All tools ready." : "Tools need attention. Set a validated path or open dependency setup.";
        }), () => IsIdle);
        InstallCommand = new(() => RunAsync(async (token, progress) =>
        {
            Status = await setup.EnsureAsync(progress, token) ? "Tools ready." : "Dependency setup incomplete.";
            var report = await dependencies.CheckAsync(DependencyRequirements.All, token);
            Tools.Clear();
            foreach (var tool in report.Tools)
            {
                Tools.Add(tool);
            }
        }), () => IsIdle);
        SetToolCommand = new(() => RunAsync(async (token, _) =>
        {
            await settings.SetToolAsync(SelectedTool, ToolPath, token);
            Status = "Validated tool path saved and applied.";
        }), () => IsIdle && !string.IsNullOrWhiteSpace(ToolPath));
        ResetToolCommand = new(() => RunAsync(async (token, _) =>
        {
            await settings.ResetToolAsync(SelectedTool, token);
            Status = "Tool override reset. Check tools to rediscover.";
        }), () => IsIdle);
    }

    public AppTheme SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (SetProperty(ref selectedTheme, value))
            {
                RaisePropertyChanged(nameof(IsSystemTheme));
                RaisePropertyChanged(nameof(IsLightTheme));
                RaisePropertyChanged(nameof(IsDarkTheme));
                themeService.ApplyTheme(value);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await settings.SetThemeAsync(value, CancellationToken.None);
                    }
                    catch
                    {
                    }
                });
            }
        }
    }

    public bool IsSystemTheme
    {
        get => selectedTheme == AppTheme.System;
        set
        {
            if (value)
            {
                SelectedTheme = AppTheme.System;
            }
        }
    }

    public bool IsLightTheme
    {
        get => selectedTheme == AppTheme.Light;
        set
        {
            if (value)
            {
                SelectedTheme = AppTheme.Light;
            }
        }
    }

    public bool IsDarkTheme
    {
        get => selectedTheme == AppTheme.Dark;
        set
        {
            if (value)
            {
                SelectedTheme = AppTheme.Dark;
            }
        }
    }

    public AppTheme[] Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    public bool AutomaticallyScanAddedFiles
    {
        get => automaticallyScanAddedFiles;
        set => SetProperty(ref automaticallyScanAddedFiles, value);
    }
    public bool UseCachedResults
    {
        get => useCachedResults;
        set => SetProperty(ref useCachedResults, value);
    }
    public bool AutoSelectAfterScan
    {
        get => autoSelectAfterScan;
        set => SetProperty(ref autoSelectAfterScan, value);
    }
    public AsyncCommand ClearCacheCommand
    {
        get;
    }
    public string Temporary
    {
        get => temporary;
        set => SetProperty(ref temporary, value);
    }
    public string Destination
    {
        get => destination;
        set => SetProperty(ref destination, value);
    }
    public bool OtherFolder
    {
        get => otherFolder;
        set => SetProperty(ref otherFolder, value);
    }
    public bool ReplaceOriginal
    {
        get => replaceOriginal;
        set => SetProperty(ref replaceOriginal, value);
    }
    public bool CreateArchive
    {
        get => createArchive;
        set => SetProperty(ref createArchive, value);
    }
    public NativeTool SelectedTool
    {
        get => selectedTool;
        set => SetProperty(ref selectedTool, value);
    }

    public string ToolPath
    {
        get => toolPath;
        set
        {
            SetProperty(ref toolPath, value);
            SetToolCommand.RaiseCanExecuteChanged();
        }
    }

    public NativeTool[] ToolNames
    {
        get;
    }
    = Enum.GetValues<NativeTool>();
    public ObservableCollection<DependencyStatus> Tools
    {
        get;
    }
    = [];
    public AsyncCommand LoadCommand
    {
        get;
    }
    public AsyncCommand SaveCommand
    {
        get;
    }
    public AsyncCommand CheckCommand
    {
        get;
    }
    public AsyncCommand InstallCommand
    {
        get;
    }
    public AsyncCommand SetToolCommand
    {
        get;
    }
    public AsyncCommand ResetToolCommand
    {
        get;
    }
    public DelegateCommand BrowseTemporaryCommand
    {
        get;
    }
    public DelegateCommand BrowseDestinationCommand
    {
        get;
    }
    public DelegateCommand BrowseToolCommand
    {
        get;
    }

    protected override void CommandsChanged()
    {
        LoadCommand?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged();
        ClearCacheCommand?.RaiseCanExecuteChanged();
        CheckCommand?.RaiseCanExecuteChanged();
        InstallCommand?.RaiseCanExecuteChanged();
        SetToolCommand?.RaiseCanExecuteChanged();
        ResetToolCommand?.RaiseCanExecuteChanged();
    }
}
