using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Traydio.Commands;
using Traydio.Services;

namespace Traydio;

public class App : Application
{
    private IPluginManager? _pluginManager;
    private ICommandRelayCoordinator? _relayCoordinator;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        TraydioTrace.Info("App", "Framework initialization started.");
        AppErrorHandler.InstallGlobalHandlers();

        if (DataTemplates.OfType<ViewLocator>().FirstOrDefault() is { } viewLocator)
        {
            viewLocator.Services = Program.Services;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var pluginManager = Program.Services.GetRequiredService<IPluginManager>();
            _pluginManager = pluginManager;
            pluginManager.Start();
            TraydioTrace.Info("App", "Plugin manager started.");

            var stationRepository = Program.Services.GetRequiredService<IStationRepository>();
            ClassicThemeService.Apply(stationRepository.ClassicThemeKey);

            var commandDispatcher = Program.Services.GetRequiredService<IAppCommandDispatcher>();
            commandDispatcher.Initialize(desktop);

            var relayCoordinator = Program.Services.GetRequiredService<ICommandRelayCoordinator>();
            _relayCoordinator = relayCoordinator;
            relayCoordinator.StartPrimaryRelay();
            TraydioTrace.Info("App", "Primary relay started.");


            var trayController = Program.Services.GetRequiredService<ITrayController>();
            trayController.Initialize(desktop);
            TraydioTrace.Info("App", "Tray controller initialized.");

            var pendingStartupCommand = Program.TakePendingStartupCommand();
            if (!string.IsNullOrWhiteSpace(pendingStartupCommand))
            {
                TraydioTrace.Info("App", "Dispatching pending startup command.");
                relayCoordinator.DispatchLocal(pendingStartupCommand);
            }

            desktop.Exit += OnDesktopExit;
        }

        TraydioTrace.Info("App", "Framework initialization completed.");
        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        TraydioTrace.Info("App", "Desktop exit cleanup started.");
        try
        {
            _relayCoordinator?.StopPrimaryRelay();
            _pluginManager?.Stop();
            TraydioTrace.Info("App", "Desktop exit cleanup completed.");
        }
        catch (Exception ex)
        {
            AppErrorHandler.Report(ex, "Desktop exit cleanup", showDialog: false);
        }
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
