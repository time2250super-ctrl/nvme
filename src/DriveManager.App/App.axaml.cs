using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DriveManager.App.Services;
using DriveManager.App.ViewModels;
using DriveManager.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DriveManager.App;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = AppServices.Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<DashboardViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };

            // Kick off the first scan once the window is up.
            vm.RefreshCommand.Execute(null);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
