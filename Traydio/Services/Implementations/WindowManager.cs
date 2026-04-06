using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Classic.CommonControls.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Traydio.Common;
using Traydio.Models;
using Traydio.Views;

namespace Traydio.Services;

public sealed class WindowManager(
    IServiceProvider serviceProvider,
    INavigationService navigationService,
    IPluginManager pluginManager,
    IPluginSettingsProvider pluginSettingsProvider,
    IStationRepository stationRepository,
    IPluginInstallDisclaimerService pluginInstallDisclaimerService,
    ILogger<WindowManager> logger) : IWindowManager
{
    private MainWindow? _mainWindow;

    public void ShowMainWindow()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowMainWindowCore, DispatcherPriority.Normal);
            return;
        }

        ShowMainWindowCore();
    }

    private void ShowMainWindowCore()
    {
        if (_mainWindow is { IsVisible: true })
        {
            TraydioTrace.Debug("WindowManager", "Activating existing main window.");
            _mainWindow.Activate();
            return;
        }

        TraydioTrace.Info("WindowManager", "Creating main window.");
        _mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        _mainWindow.Closed += (_, _) => _mainWindow = null;
        _mainWindow.Show();
    }

    public void ShowStationManager()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowStationManager, DispatcherPriority.Normal);
            return;
        }

        ShowMainWindowCore();
        TraydioTrace.Debug("WindowManager", "Navigating to Stations page.");
        navigationService.Navigate(AppPage.Stations);
    }

    public void ShowStationSearch()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowStationSearch, DispatcherPriority.Normal);
            return;
        }

        ShowMainWindowCore();
        TraydioTrace.Debug("WindowManager", "Navigating to Search page.");
        navigationService.Navigate(AppPage.Search);
    }

    public void ShowPluginManager()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowPluginManager, DispatcherPriority.Normal);
            return;
        }

        ShowMainWindowCore();
        TraydioTrace.Debug("WindowManager", "Navigating to Plugins page.");
        navigationService.Navigate(AppPage.Plugins);
        ShowPluginSafetyWarningIfNeededAsync().ForgetWithErrorHandling("Show plugin safety warning", showDialog: false);
    }

    private async Task ShowPluginSafetyWarningIfNeededAsync()
    {
        var settings = stationRepository.StationDiscoveryPlugins;
        if (settings.HasShownPluginSafetyWarning)
        {
            return;
        }

        if (_mainWindow is null)
        {
            return;
        }

        await MessageBox.ShowDialog(
            _mainWindow,
            "Plugins can execute external code. Only install plugins from authors you trust and review source when possible.",
            "Plugin safety warning",
            MessageBoxButtons.Ok,
            MessageBoxIcon.Warning);

        stationRepository.SaveStationDiscoveryPluginSettings(new StationDiscoveryPluginSettings
        {
            PluginDirectory = settings.PluginDirectory,
            DisabledPluginIds = settings.DisabledPluginIds,
            PendingDeletePluginPaths = settings.PendingDeletePluginPaths,
            HasShownPluginSafetyWarning = true,
        });
    }

    public void ShowSettings()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowSettings, DispatcherPriority.Normal);
            return;
        }

        ShowMainWindowCore();
        TraydioTrace.Debug("WindowManager", "Navigating to Settings page.");
        navigationService.Navigate(AppPage.Settings);
    }

    public void ShowCommandTester()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowCommandTester, DispatcherPriority.Normal);
            return;
        }

        ShowMainWindowCore();

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

        if (_mainWindow is { IsVisible: true })
        {
            TraydioTrace.Debug("WindowManager", "Showing command tester as dialog.");
            window.ShowDialog(_mainWindow).ForgetWithErrorHandling("Show command tester dialog", showDialog: true);
            return;
        }

        TraydioTrace.Debug("WindowManager", "Showing command tester as top-level window.");
        window.Show();
    }

    public bool ShowPluginSettings(string pluginId, out string? error)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            string? marshaledError = null;
            var result = Dispatcher.UIThread
                .InvokeAsync(() => ShowPluginSettingsCore(pluginId, out marshaledError), DispatcherPriority.Normal)
                .GetAwaiter()
                .GetResult();
            error = marshaledError;
            return result;
        }

        return ShowPluginSettingsCore(pluginId, out error);
    }

    private bool ShowPluginSettingsCore(string pluginId, out string? error)
    {
        error = null;
        var plugin = pluginManager.GetPlugins()
            .FirstOrDefault(p => string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase));

        if (plugin is null)
        {
            error = "Plugin not found.";
            logger.LogWarning("{Error} pluginId={PluginId}", error, pluginId);
            return false;
        }

        var settingsCapability = plugin.Capabilities.OfType<IPluginSettingsCapability>().FirstOrDefault();
        if (settingsCapability is null)
        {
            error = "This plugin does not expose a settings page.";
            logger.LogWarning("{Error} pluginId={PluginId}", error, plugin.Id);
            return false;
        }

        var settingsAccessor = new PluginSettingsAccessor(
            pluginSettingsProvider,
            pluginInstallDisclaimerService,
            plugin.Id);

        object? content;
        try
        {
            content = settingsCapability.CreateSettingsView(settingsAccessor);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create settings view for pluginId={PluginId}", plugin.Id);
            content = CreatePluginSettingsFailureView(plugin.DisplayName, ex);
        }

        if (content is not Control control)
        {
            error = "Plugin returned an unsupported settings view.";
            logger.LogWarning("{Error} pluginId={PluginId}", error, plugin.Id);
            return false;
        }

        ShowMainWindowCore();
        TraydioTrace.Debug("WindowManager", "Opening plugin settings for pluginId=" + plugin.Id);

        var baseTitle = $"{plugin.DisplayName} Settings";
        var settingsWindow = new Window
        {
            Title = baseTitle,
            Width = 720,
            Height = 420,
            Content = control,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        WindowThemeHelper.ApplyClassicWindowTheme(settingsWindow);

        void UpdateTitle()
        {
            settingsWindow.Title = settingsAccessor.HasPendingChanges
                ? baseTitle + '*'
                : baseTitle;
        }

        settingsAccessor.PendingChangesChanged += UpdateTitle;
        settingsWindow.Closed += (_, _) => settingsAccessor.PendingChangesChanged -= UpdateTitle;
        UpdateTitle();

        var isHandlingClosePrompt = false;
        settingsWindow.Closing += (_, args) =>
        {
            if (isHandlingClosePrompt || !settingsAccessor.HasPendingChanges)
            {
                return;
            }

            args.Cancel = true;
            isHandlingClosePrompt = true;
            PromptToSaveChangesAsync().ForgetWithErrorHandling("Plugin settings close prompt", showDialog: false);

            async Task PromptToSaveChangesAsync()
            {
                try
                {
                    var choice = await MessageBox.ShowDialog(
                        settingsWindow,
                        "Save changes to this plugin's settings before closing?",
                        "Save plugin settings",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);

                    switch (choice)
                    {
                        case MessageBoxResult.Yes:
                            settingsAccessor.Commit();
                            settingsWindow.Close();
                            break;
                        case MessageBoxResult.No:
                            settingsAccessor.Discard();
                            settingsWindow.Close();
                            break;
                    }
                }
                finally
                {
                    isHandlingClosePrompt = false;
                }
            }
        };

        if (_mainWindow is { IsVisible: true })
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
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowAboutDialog, DispatcherPriority.Normal);
            return;
        }

        ShowMainWindowCore();

        if (_mainWindow is null)
        {
            return;
        }

        using var iconStream = AssetLoader.Open(new Uri("avares://Traydio/Assets/stations.ico"));
        var bitmap = new Bitmap(iconStream);
        const int INITIAL_COPYRIGHT_YEAR = 2026;
        var buildYear = ViewLocator.BuildYear;
        var copyright = buildYear <= INITIAL_COPYRIGHT_YEAR
            ? $"Copyright (C) {INITIAL_COPYRIGHT_YEAR}"
            : $"Copyright (C) {INITIAL_COPYRIGHT_YEAR} - {buildYear}";

        AboutDialog.ShowDialog(
            _mainWindow,
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

    private sealed class PluginSettingsAccessor(
        IPluginSettingsProvider pluginSettingsProvider,
        IPluginInstallDisclaimerService pluginInstallDisclaimerService,
        string pluginId) : IPluginSettingsAccessor
    {
        private readonly Dictionary<string, string> _values = new(
            pluginSettingsProvider.GetPluginSettings(pluginId),
            StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _originalValues = new(
            pluginSettingsProvider.GetPluginSettings(pluginId),
            StringComparer.OrdinalIgnoreCase);

        public bool HasPendingChanges => !DictionaryEquals(_values, _originalValues);

        public event Action? PendingChangesChanged;

        public string? GetValue(string key)
        {
            return _values.GetValueOrDefault(key);
        }

        public void SetValue(string key, string? value)
        {
            var hadPendingChanges = HasPendingChanges;

            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                _values.Remove(key.Trim());
                NotifyPendingChangesChangedIfNeeded(hadPendingChanges);
                return;
            }

            _values[key.Trim()] = value.Trim();
            NotifyPendingChangesChangedIfNeeded(hadPendingChanges);
        }

        public void Save()
        {
            // Deferred commit: actual persistence happens from the host close prompt.
        }

        public void Commit()
        {
            var hadPendingChanges = HasPendingChanges;
            pluginSettingsProvider.SavePluginSettings(pluginId, _values);
            _originalValues.Clear();
            foreach (var pair in _values)
            {
                _originalValues[pair.Key] = pair.Value;
            }

            NotifyPendingChangesChangedIfNeeded(hadPendingChanges);
        }

        public void Discard()
        {
            var hadPendingChanges = HasPendingChanges;
            _values.Clear();
            foreach (var pair in _originalValues)
            {
                _values[pair.Key] = pair.Value;
            }

            NotifyPendingChangesChangedIfNeeded(hadPendingChanges);
        }

        public Task<bool> ShowInstallDisclaimerAsync(string targetPluginId, PluginInstallDisclaimer disclaimer, bool requireAcceptance)
        {
            return pluginInstallDisclaimerService.ShowAsync(disclaimer, requireAcceptance, CancellationToken.None);
        }

        private static bool DictionaryEquals(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var other) || !string.Equals(pair.Value, other, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void NotifyPendingChangesChangedIfNeeded(bool previous)
        {
            if (previous != HasPendingChanges)
            {
                PendingChangesChanged?.Invoke();
            }
        }
    }
}
