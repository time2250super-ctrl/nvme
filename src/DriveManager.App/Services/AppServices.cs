using System;
using System.IO;
using DriveManager.Ai;
using DriveManager.App.ViewModels;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Telemetry;
using DriveManager.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace DriveManager.App.Services;

/// <summary>
/// Composition root. Registers platform services behind their Core abstractions so the UI
/// stays platform-agnostic.
/// </summary>
public static class AppServices
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // AI (platform independent). Models live in a portable "models" folder next to the exe.
        services.AddSingleton<IHealthAnalyzer, RuleBasedHealthAnalyzer>();
        var modelsDir = Path.Combine(AppContext.BaseDirectory, "models");
        services.AddSingleton<IModelManager>(_ => new LocalModelManager(modelsDir));
        services.AddSingleton<IChatAssistant, LlamaChatAssistant>();

        // Telemetry history (SQLite). Portable: stored next to the executable so the whole app
        // can live and run from its own dedicated NVMe partition with no external dependencies.
        var dbPath = Path.Combine(AppContext.BaseDirectory, "data", "telemetry.db");
        services.AddSingleton<ITelemetryStore>(_ => new SqliteTelemetryStore(dbPath));

        // Platform services.
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IDiskService, WindowsDiskService>();
            services.AddSingleton<IHealthService, WindowsHealthService>();
            services.AddSingleton<IFormatService, WindowsFormatService>();
            services.AddSingleton<ISecurityService, WindowsSecurityService>();
            services.AddSingleton<IDriveToolsService, WindowsDriveToolsService>();
            services.AddSingleton<IPermissionManager, WindowsPermissionManager>();
        }

        // View models.
        services.AddTransient<DashboardViewModel>();

        return services.BuildServiceProvider();
    }
}
