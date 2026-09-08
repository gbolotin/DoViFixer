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
        services.AddTransient<DependencyService>();
        services.AddTransient<InspectionService>();
        services.AddTransient<ScanService>();
        services.AddTransient<ConversionPlanner>();
        services.AddTransient<ConversionService>();
        services.AddTransient<BatchConversionService>();
        services.AddTransient<BackupService>();
        services.AddTransient<RestoreService>();
        services.AddTransient<CleanupService>();
        services.AddTransient<UpdateService>();
        services.AddTransient<SettingsService>();
        return services;
    }
}
