using DoViFixer.Application.Backup;
using DoViFixer.Application.Cleanup;
using DoViFixer.Application.Conversion;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Restore;
using DoViFixer.Application.Scanning;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace DoViFixer.Application;
public static class DependencyInjection
{
    public static IServiceCollection AddDoViFixerApplication(this IServiceCollection services)
    {
        // One feed for the process, so every operation's entries reach the same activity list.
        services.AddSingleton<Abstractions.IActivityFeed, Activity.ActivityFeed>();
        services.AddTransient<DependencyService>();
        services.AddTransient<InspectionService>();
        services.AddTransient<Library.MediaLibraryService>();
        services.AddTransient<ScanService>();
        services.AddTransient<ConversionPlanner>();
        services.AddTransient<ConversionService>();
        services.AddTransient<ConversionDiskBudget>();
        services.AddTransient<BatchConversionService>();
        services.AddTransient<Operations.ControlledBatchService>();
        services.AddTransient<BackupService>();
        services.AddTransient<RestoreService>();
        services.AddTransient<CleanupService>();
        services.AddTransient<UpdateService>();
        services.AddTransient<SettingsService>();
        services.AddTransient<PresetService>();
        services.AddTransient<Diagnostics.DiagnosticsService>();
        services.AddTransient<Reports.ReportService>();
        return services;
    }
}
