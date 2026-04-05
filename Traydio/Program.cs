using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Traydio.Commands;
using Traydio.Common;
using Traydio.Services;
using Traydio.Services.Implementations;
using Traydio.ViewModels;
using Traydio.Views;

[assembly: GenerateViewLocator("Traydio", "ViewLocator", ServiceProviderPropertyName = "Services")]

namespace Traydio;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
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

        var commandRelayCoordinator = services.GetRequiredService<ICommandRelayCoordinator>();
        var instanceGate = services.GetRequiredService<IInstanceGate>();
        var startupCommandBridges = services.GetServices<IStartupCommandBridge>();
        var startupCommand = ParseStartupCommand(args, startupCommandBridges);

        if (!instanceGate.TryAcquire())
        {
            commandRelayCoordinator.TryRelayToPrimary(startupCommand ?? "open");
            return;
        }

        if (!string.IsNullOrWhiteSpace(startupCommand))
        {
            commandRelayCoordinator.DispatchLocal(startupCommand);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(
            args,
            ShutdownMode.OnExplicitShutdown);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static IServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IStationRepository, StationRepository>();
        services.AddSingleton<IRadioPlayer>(CreateRadioPlayer);
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IWindowManager, WindowManager>();
        services.AddSingleton<IAppCommandDispatcher, AppCommandDispatcher>();
        services.AddSingleton<ICommandTextRouter, CommandTextRouter>();
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IProtocolRegistrationService, WindowsProtocolRegistrationService>();
        }
        else
        {
            services.AddSingleton<IProtocolRegistrationService, NoOpProtocolRegistrationService>();
        }
        services.AddSingleton<IPluginManager, PluginManager>();
        services.AddSingleton<IStationDiscoveryService, StationDiscoveryService>();
        services.AddSingleton<IInstanceGate, MutexInstanceGate>();
        services.AddSingleton<ICommandRelayClient, NamedPipeCommandRelayClient>();
        services.AddSingleton<ICommandRelayClient, LoopbackCommandRelayClient>();
        services.AddSingleton<ICommandRelayServer, NamedPipeCommandRelayServer>();
        services.AddSingleton<ICommandRelayServer, LoopbackCommandRelayServer>();
        services.AddSingleton<IStartupCommandBridge, ProtocolUrlStartupCommandBridge>();
        services.AddSingleton<IStartupCommandBridge, CommandLineStartupCommandBridge>();
        services.AddSingleton<ICommandRelayCoordinator, CommandRelayCoordinator>();
        services.AddSingleton<ITrayController, TrayController>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<StationManagerPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<PluginManagementWindowViewModel>();
        services.AddTransient<StationSearchWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }

    private static IRadioPlayer CreateRadioPlayer(IServiceProvider serviceProvider)
    {
        var stationRepository = serviceProvider.GetRequiredService<IStationRepository>();
        var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();

        var engines = pluginManager.GetPlugins()
            .SelectMany(static plugin => plugin.Capabilities.OfType<IRadioPlayerEngineCapability>())
            .GroupBy(static capability => capability.EngineId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        if (engines.Length == 0)
        {
            throw new InvalidOperationException("No radio player engines were found. Ensure at least one playback plugin is available.");
        }

        var requestedEngineId = stationRepository.RadioPlayerEngineId;
        var selectedEngine = !string.IsNullOrWhiteSpace(requestedEngineId)
            ? engines.FirstOrDefault(engine => string.Equals(engine.EngineId, requestedEngineId, StringComparison.OrdinalIgnoreCase))
            : null;

        selectedEngine ??= engines.First();

        var player = selectedEngine.CreatePlayer();
        player.SetVolume(stationRepository.Volume);
        player.SetAudioOutputDevice(stationRepository.AudioOutputDeviceId);

        if (!string.Equals(stationRepository.RadioPlayerEngineId, selectedEngine.EngineId, StringComparison.OrdinalIgnoreCase))
        {
            stationRepository.RadioPlayerEngineId = selectedEngine.EngineId;
        }

        return player;
    }

    private static string? ParseStartupCommand(string[] args, IEnumerable<IStartupCommandBridge> bridges)
    {
        if (args.Length == 0)
        {
            return null;
        }

        foreach (var bridge in bridges)
        {
            if (bridge.TryGetCommand(args, out var commandText) && !string.IsNullOrWhiteSpace(commandText))
            {
                return commandText;
            }
        }

        return string.Join(" ", args);
    }
}
