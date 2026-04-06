using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Classic.CommonControls;
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
    private static string? _pendingStartupCommand;

    public static IServiceProvider Services => _services
        ?? throw new InvalidOperationException("Service provider is not initialized.");

    public static string? TakePendingStartupCommand()
    {
        var command = _pendingStartupCommand;
        _pendingStartupCommand = null;
        return command;
    }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            TraydioTrace.Info("Program", "Startup begin.");

            var hasDebuggerLaunchArg = false;
            foreach (var arg in args)
            {
                if (!string.Equals(arg, "--debugger-launch", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                hasDebuggerLaunchArg = true;
                break;
            }

            if (hasDebuggerLaunchArg && !Debugger.IsAttached)
            {
                try
                {
                    Debugger.Launch();
                }
                catch
                {
                    // Best effort only; restart should continue even if debugger launch fails.
                }
            }

            using var services = ConfigureServices().BuildServiceProvider();
            _services = services;

            var commandRelayCoordinator = services.GetRequiredService<ICommandRelayCoordinator>();
            var instanceGate = services.GetRequiredService<IInstanceGate>();
            var startupCommandBridges = services.GetServices<IStartupCommandBridge>();
            var startupCommand = ParseStartupCommand(args, startupCommandBridges);
            TraydioTrace.Debug("Program", "Parsed startup command: " + (startupCommand ?? "<none>"));

            if (!instanceGate.TryAcquire())
            {
                TraydioTrace.Info("Program", "Secondary instance detected, relaying command to primary.");
                commandRelayCoordinator.TryRelayToPrimary(startupCommand ?? "open");
                return;
            }

            _pendingStartupCommand = startupCommand;
            TraydioTrace.Debug("Program", "Stored pending startup command for post-init dispatch.");

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(
                args,
                ShutdownMode.OnExplicitShutdown);
            TraydioTrace.Info("Program", "Desktop lifetime exited.");
        }
        catch (Exception ex)
        {
            AppErrorHandler.Report(ex, "Program startup", showDialog: false);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseMessageBoxSounds()
            .LogToTrace();

    private static IServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IStationRepository, StationRepository>();
        services.AddSingleton<IPluginSettingsProvider, PluginSettingsProvider>();
        services.AddSingleton<IRadioPlayer, DeferredPluginRadioPlayer>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IWindowManager, WindowManager>();
        services.AddSingleton<IAppCommandDispatcher, AppCommandDispatcher>();
        services.AddSingleton<ICommandTextRouter, CommandTextRouter>();
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IProtocolRegistrationService, WindowsProtocolRegistrationService>();
            services.AddSingleton<IWmicExtendedFunctionalityService, WindowsWmicExtendedFunctionalityService>();
        }
        else
        {
            services.AddSingleton<IProtocolRegistrationService, NoOpProtocolRegistrationService>();
            services.AddSingleton<IWmicExtendedFunctionalityService, NoOpWmicExtendedFunctionalityService>();
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
        services.AddTransient<CommandTesterPage>();
        services.AddTransient<MainWindow>();

        return services;
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
