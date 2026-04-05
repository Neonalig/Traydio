using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Traydio.Common;
using Traydio.Views;

namespace Traydio.Services;

public sealed class WindowManager(
    IServiceProvider serviceProvider,
    INavigationService navigationService,
    IPluginManager pluginManager,
    IPluginSettingsProvider pluginSettingsProvider) : IWindowManager
{
    private MainWindow? _mainWindow;

    public void ShowMainWindow()
    {
        if (_mainWindow is { IsVisible: true })
        {
            _mainWindow.Activate();
            return;
        }

        _mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        _mainWindow.Closed += (_, _) => _mainWindow = null;
        _mainWindow.Show();
    }

    public void ShowStationManager()
    {
        ShowMainWindow();
        navigationService.Navigate(AppPage.Stations);
    }

    public void ShowStationSearch()
    {
        ShowMainWindow();
        navigationService.Navigate(AppPage.Search);
    }

    public void ShowPluginManager()
    {
        ShowMainWindow();
        navigationService.Navigate(AppPage.Plugins);
    }

    public void ShowSettings()
    {
        ShowMainWindow();
        navigationService.Navigate(AppPage.Settings);
    }

    public bool ShowPluginSettings(string pluginId, out string? error)
    {
        error = null;
        var plugin = pluginManager.GetPlugins()
            .FirstOrDefault(p => string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase));

        if (plugin is null)
        {
            error = "Plugin not found.";
            Console.Error.WriteLine($"[Traydio][PluginSettings] {error} pluginId={pluginId}");
            return false;
        }

        var settingsCapability = plugin.Capabilities.OfType<IPluginSettingsCapability>().FirstOrDefault();
        if (settingsCapability is null)
        {
            error = "This plugin does not expose a settings page.";
            Console.Error.WriteLine($"[Traydio][PluginSettings] {error} pluginId={plugin.Id}");
            return false;
        }

        object? content;
        try
        {
            content = settingsCapability.CreateSettingsView(new PluginSettingsAccessor(pluginSettingsProvider, plugin.Id));
        }
        catch (Exception ex)
        {
            error = "Plugin settings failed to open: " + ex.Message;
            Console.Error.WriteLine($"[Traydio][PluginSettings] Failed to create settings view for pluginId={plugin.Id}: {ex}");
            return false;
        }

        if (content is not Control control)
        {
            error = "Plugin returned an unsupported settings view.";
            Console.Error.WriteLine($"[Traydio][PluginSettings] {error} pluginId={plugin.Id}");
            return false;
        }

        ShowMainWindow();

        var settingsWindow = new Window
        {
            Title = $"{plugin.DisplayName} Settings",
            Width = 720,
            Height = 420,
            Content = control,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        if (_mainWindow is not null)
        {
            _ = settingsWindow.ShowDialog(_mainWindow);
        }
        else
        {
            settingsWindow.Show();
        }

        return true;
    }

    private sealed class PluginSettingsAccessor(IPluginSettingsProvider pluginSettingsProvider, string pluginId) : IPluginSettingsAccessor
    {
        private readonly Dictionary<string, string> _values = new(
            pluginSettingsProvider.GetPluginSettings(pluginId),
            StringComparer.OrdinalIgnoreCase);

        public string? GetValue(string key)
        {
            return _values.TryGetValue(key, out var value) ? value : null;
        }

        public void SetValue(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                _values.Remove(key.Trim());
                return;
            }

            _values[key.Trim()] = value.Trim();
        }

        public void Save()
        {
            pluginSettingsProvider.SavePluginSettings(pluginId, _values);
        }
    }
}

