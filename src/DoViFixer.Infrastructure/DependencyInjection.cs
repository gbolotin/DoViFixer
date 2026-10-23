using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Updates;
using DoViFixer.Infrastructure.Archives;
using DoViFixer.Infrastructure.Configuration;
using DoViFixer.Infrastructure.Library;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.Diagnostics;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.MediaTools;
using DoViFixer.Infrastructure.MediaTools.Processes;
using DoViFixer.Infrastructure.TemporaryStorage;
using DoViFixer.Infrastructure.Updates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoViFixer.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddDoViFixerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        string root = configuration["DoViFixer:DataDirectory"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoViFixer");
        services.AddSingleton(new StorageOptions(Path.GetFullPath(root)));
        services.AddSingleton(new DiagnosticOptions(Path.GetFullPath(configuration["DoViFixer:Logging:Directory"] ?? Path.Combine(root, "Logs"))));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IPresetStore, PresetStore>();
        services.AddSingleton<IHardwareInfoProvider, HardwareInfoProvider>();
        services.AddSingleton<MediaLibrary>();
        services.AddSingleton<IMediaLibrary>(provider => provider.GetRequiredService<MediaLibrary>());
        services.AddSingleton<IAnalysisCache>(provider => provider.GetRequiredService<MediaLibrary>());
        services.AddTransient<IUserPathRegistration, UserPathRegistration>();
        services.AddTransient<IFileExplorerMenu, FileExplorerMenu>();
        services.AddSingleton<IToolCatalog, ToolCatalog>();
        services.AddSingleton(_ =>
        {
            var http = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{ApplicationTitle.Name}/{ApplicationVersion.Of(typeof(DependencyInjection).Assembly)}");
            return http;
        });
        services.AddTransient<IProcessRunner, ProcessRunner>();
        services.AddTransient<IDependencyDetector, DependencyDetector>();
        services.AddTransient<IDependencyInstaller, DependencyInstaller>();
        services.AddTransient<IFileOperations, FileOperations>();
        services.AddTransient<IFileDiscovery, FileOperations>();
        services.AddTransient<ISourceFileMonitor, SourceFileMonitor>();
        services.AddTransient<IFolderWatcher, FolderWatcher>();
        services.AddTransient<ITemporaryWorkspaceFactory, TemporaryWorkspaceFactory>();
        services.AddTransient<IOutputPublisher, OutputPublisher>();
        services.AddTransient<IFileLockInspector, WindowsFileLockInspector>();
        services.AddTransient<IDiagnosticBundleWriter, DiagnosticBundleWriter>();
        services.AddTransient<IReportWriter, Reports.ReportWriter>();
        services.AddTransient<MediaProbe>();
        services.AddTransient<IMediaProbe, MediaProbe>();
        services.AddSingleton<IMediaPreview, MediaPreview>();
        services.AddTransient<VideoProcessor>();
        services.AddTransient<IVideoProcessor, VideoProcessor>();
        services.AddTransient<IMediaVerifier, MediaVerifier>();
        services.AddTransient<IBackupArchiveStore, BackupArchiveStore>();
        services.AddTransient<IUpdateSource, GitHubReleaseSource>();
        return services;
    }
}
