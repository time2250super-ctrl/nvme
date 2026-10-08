using System;
using DriveManager.Ai;
using DriveManager.App.ViewModels;
using DriveManager.Core.Abstractions;
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

        // AI (platform independent).
        services.AddSingleton<IHealthAnalyzer, RuleBasedHealthAnalyzer>();
        services.AddSingleton<IChatAssistant, PlaceholderChatAssistant>();

        // Platform services.
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IDiskService, WindowsDiskService>();
            services.AddSingleton<IHealthService, WindowsHealthService>();
            services.AddSingleton<IFormatService, WindowsFormatService>();
            services.AddSingleton<ISecurityService, WindowsSecurityService>();
        }

        // View models.
        services.AddTransient<DashboardViewModel>();

        return services.BuildServiceProvider();
    }
}
