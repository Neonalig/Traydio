using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using System;
using Traydio.Commands;
using Traydio.Common;
using Traydio.Services;
using Traydio.ViewModels;
using Traydio.Views;

[assembly: GenerateViewLocator("Traydio", "ViewLocator", ServiceProviderPropertyName = "Services")]

namespace Traydio;

sealed class Program
{
    private static ServiceProvider? _services;

    public static IServiceProvider Services => _services
        ?? throw new InvalidOperationException("Service provider is not initialized.");

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var services = ConfigureServices().BuildServiceProvider();
        _services = services;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(
            args,
            ShutdownMode.OnExplicitShutdown);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static IServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IStationRepository, StationRepository>();
        services.AddSingleton<IRadioPlayer, LibVlcRadioPlayer>();
        services.AddSingleton<IWindowManager, WindowManager>();
        services.AddSingleton<IAppCommandDispatcher, AppCommandDispatcher>();
        services.AddSingleton<ICommandTextRouter, CommandTextRouter>();
        services.AddSingleton<ITrayController, TrayController>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }
}
