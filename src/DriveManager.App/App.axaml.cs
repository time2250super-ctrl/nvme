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
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;

            window.Opened += async (_, _) =>
            {
                try
                {
                    if (vm.RefreshCommand.CanExecute(null))
                        await vm.RefreshCommand.ExecuteAsync(null);
                }
                catch (Exception ex)
                {
                    vm.Status = "Scan failed: " + ex.Message;
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
