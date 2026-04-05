using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Traydio.Commands;
using Traydio.Services;

namespace Traydio;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (DataTemplates.OfType<ViewLocator>().FirstOrDefault() is { } viewLocator)
        {
            viewLocator.Services = Program.Services;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var commandDispatcher = Program.Services.GetRequiredService<IAppCommandDispatcher>();
            commandDispatcher.Initialize(desktop);

            var relayCoordinator = Program.Services.GetRequiredService<ICommandRelayCoordinator>();
            relayCoordinator.StartPrimaryRelay();

            var trayController = Program.Services.GetRequiredService<ITrayController>();
            trayController.Initialize(desktop);

            desktop.Exit += (_, _) => relayCoordinator.StopPrimaryRelay();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
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
