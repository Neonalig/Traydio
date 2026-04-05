using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Classic.CommonControls.Dialogs;
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

    public void ShowCommandTester()
    {
        ShowMainWindow();

        var page = serviceProvider.GetRequiredService<CommandTesterPage>();
        var window = new Window
        {
            Title = "Commands",
            Width = 760,
            Height = 520,
            Content = page,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        WindowThemeHelper.ApplyClassicWindowTheme(window);

        if (_mainWindow is not null)
        {
            window.ShowDialog(_mainWindow).ForgetWithErrorHandling("Show command tester dialog", showDialog: true);
            return;
        }

        window.Show();
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
            Console.Error.WriteLine($"[Traydio][PluginSettings] Failed to create settings view for pluginId={plugin.Id}: {ex}");
            content = CreatePluginSettingsFailureView(plugin.DisplayName, ex);
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
        WindowThemeHelper.ApplyClassicWindowTheme(settingsWindow);

        if (_mainWindow is not null)
        {
            settingsWindow.ShowDialog(_mainWindow).ForgetWithErrorHandling("Show plugin settings dialog", showDialog: true);
        }
        else
        {
            settingsWindow.Show();
        }

        return true;
    }

    public void ShowAboutDialog()
    {
        ShowMainWindow();

        using var iconStream = AssetLoader.Open(new Uri("avares://Traydio/Assets/Icons9x/stations.ico"));
        var bitmap = new Bitmap(iconStream);
        const int INITIAL_COPYRIGHT_YEAR = 2026;
        var buildYear = ViewLocator.BuildYear;
        var copyright = buildYear <= INITIAL_COPYRIGHT_YEAR
            ? $"Copyright (C) {INITIAL_COPYRIGHT_YEAR}"
            : $"Copyright (C) {INITIAL_COPYRIGHT_YEAR} - {buildYear}";

        AboutDialog.ShowDialog(
            _mainWindow!,
            new AboutDialogOptions
            {
                Title = "Traydio",
                Copyright = copyright,
                Icon = bitmap
            }
        ).ForgetWithErrorHandling("Show about dialog", showDialog: true);
    }

    private static Control CreatePluginSettingsFailureView(string pluginDisplayName, Exception ex)
    {
        return new StackPanel
        {
            Margin = new Avalonia.Thickness(12),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = $"{pluginDisplayName} settings could not be loaded.",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "The plugin threw an exception while creating its settings view.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = ex.GetType().Name + ": " + ex.Message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
            },
        };
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
