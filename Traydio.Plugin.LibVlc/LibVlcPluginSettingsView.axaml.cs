using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Traydio.Common;

namespace Traydio.Plugin.LibVlc;

public partial class LibVlcPluginSettingsView : UserControl
{
    private readonly IPluginSettingsAccessor _settingsAccessor;
    private readonly ComboBox _outputModuleComboBox;
    private readonly ComboBox _outputDeviceComboBox;
    private readonly TextBlock _statusText;
    private readonly bool _isInitializing;

    public LibVlcPluginSettingsView()
        : this(new NullPluginSettingsAccessor())
    {
    }

    public LibVlcPluginSettingsView(IPluginSettingsAccessor settingsAccessor)
    {
        _settingsAccessor = settingsAccessor;
        AvaloniaXamlLoader.Load(this);

        _outputModuleComboBox = this.FindControl<ComboBox>("OutputModuleComboBox")
            ?? throw new InvalidOperationException("LibVLC settings view is missing OutputModuleComboBox.");
        _outputDeviceComboBox = this.FindControl<ComboBox>("OutputDeviceComboBox")
            ?? throw new InvalidOperationException("LibVLC settings view is missing OutputDeviceComboBox.");
        _statusText = this.FindControl<TextBlock>("StatusText")
            ?? throw new InvalidOperationException("LibVLC settings view is missing StatusText.");

        _isInitializing = true;
        var savedModule = _settingsAccessor.GetValue(LibVlcPluginSettings.OUTPUT_MODULE_KEY);
        var savedDevice = _settingsAccessor.GetValue(LibVlcPluginSettings.OUTPUT_DEVICE_ID_KEY);

        var moduleOptions = BuildModuleOptions(savedModule);
        _outputModuleComboBox.ItemsSource = moduleOptions;
        _outputModuleComboBox.SelectedItem = FindOption(moduleOptions, savedModule);

        var selectedModuleValue = (_outputModuleComboBox.SelectedItem as OptionItem)?.Value;
        var deviceOptions = BuildDeviceOptions(selectedModuleValue, savedDevice);
        _outputDeviceComboBox.ItemsSource = deviceOptions;
        _outputDeviceComboBox.SelectedItem = FindOption(deviceOptions, savedDevice);
        _isInitializing = false;

        _outputModuleComboBox.SelectionChanged += OnModuleChanged;
        _outputDeviceComboBox.SelectionChanged += (_, _) => UpdatePendingSettings();
    }

    private void OnModuleChanged(object? sender, SelectionChangedEventArgs e)
    {
        var currentDevice = (_outputDeviceComboBox.SelectedItem as OptionItem)?.Value
                            ?? _settingsAccessor.GetValue(LibVlcPluginSettings.OUTPUT_DEVICE_ID_KEY);
        var moduleValue = (_outputModuleComboBox.SelectedItem as OptionItem)?.Value;
        var deviceOptions = BuildDeviceOptions(moduleValue, currentDevice);
        _outputDeviceComboBox.ItemsSource = deviceOptions;
        _outputDeviceComboBox.SelectedItem = FindOption(deviceOptions, currentDevice);

        UpdatePendingSettings();
    }

    private void UpdatePendingSettings()
    {
        if (_isInitializing)
        {
            return;
        }

        var module = (_outputModuleComboBox.SelectedItem as OptionItem)?.Value;
        var deviceId = (_outputDeviceComboBox.SelectedItem as OptionItem)?.Value;

        _settingsAccessor.SetValue(LibVlcPluginSettings.OUTPUT_MODULE_KEY, string.IsNullOrWhiteSpace(module) ? null : module);
        _settingsAccessor.SetValue(LibVlcPluginSettings.OUTPUT_DEVICE_ID_KEY, string.IsNullOrWhiteSpace(deviceId) ? null : deviceId);
    }

    private static IReadOnlyList<OptionItem> BuildModuleOptions(string? savedModule)
    {
        var options = new List<OptionItem>
        {
            new(null, "System default"),
        };

        if (OperatingSystem.IsWindows())
        {
            options.AddRange([
                new OptionItem("mmdevice", "mmdevice"),
                new OptionItem("directsound", "directsound"),
                new OptionItem("waveout", "waveout"),
                new OptionItem("wasapi", "wasapi"),
            ]);
        }
        else if (OperatingSystem.IsLinux())
        {
            options.AddRange([
                new OptionItem("pulse", "pulse"),
                new OptionItem("alsa", "alsa"),
                new OptionItem("jack", "jack"),
            ]);
        }
        else if (OperatingSystem.IsMacOS())
        {
            options.AddRange([
                new OptionItem("coreaudio", "coreaudio"),
                new OptionItem("auhal", "auhal"),
            ]);
        }

        AddCustomOptionIfNeeded(options, savedModule);
        return options;
    }

    private static IReadOnlyList<OptionItem> BuildDeviceOptions(string? module, string? savedDevice)
    {
        var options = new List<OptionItem>
        {
            new(null, "System default"),
        };

        if (OperatingSystem.IsWindows())
        {
            if (string.Equals(module, "waveout", StringComparison.OrdinalIgnoreCase))
            {
                options.AddRange([
                    new OptionItem("default", "Default device"),
                    new OptionItem("communications", "Default communications device"),
                ]);
            }
            else
            {
                options.AddRange([
                    new OptionItem("default", "Default device"),
                    new OptionItem("communications", "Default communications device"),
                ]);
            }
        }
        else
        {
            options.Add(new OptionItem("default", "Default device"));
        }

        AddCustomOptionIfNeeded(options, savedDevice);
        return options;
    }

    private static void AddCustomOptionIfNeeded(List<OptionItem> options, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (options.Any(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        options.Add(new OptionItem(value, "Saved custom: " + value));
    }

    private static OptionItem FindOption(IReadOnlyList<OptionItem> options, string? value)
    {
        return options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
               ?? options.First();
    }

    private void OnOpenWebsiteClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LibVlcPlugin.SettingsDisclaimer.LinkUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LibVlcPlugin.SettingsDisclaimer.LinkUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _statusText.Text = "Could not open browser: " + ex.Message;
        }
    }

    private void OnShowDisclaimerClick(object? sender, RoutedEventArgs e)
    {
        _ = ShowDisclaimerAsync();
    }

    private async Task ShowDisclaimerAsync()
    {
        try
        {
            var shown = await _settingsAccessor.ShowInstallDisclaimerAsync(
                LibVlcPlugin.PLUGIN_ID,
                LibVlcPlugin.SettingsDisclaimer,
                requireAcceptance: false);
            if (!shown)
            {
                _statusText.Text = "Could not display disclaimer dialog.";
            }
        }
        catch (Exception ex)
        {
            _statusText.Text = "Could not display disclaimer dialog: " + ex.Message;
        }
    }

    private sealed class NullPluginSettingsAccessor : IPluginSettingsAccessor
    {
        public string? GetValue(string key) => null;

        public void SetValue(string key, string? value)
        {
        }

        public void Save()
        {
        }

        public Task<bool> ShowInstallDisclaimerAsync(string pluginId, PluginInstallDisclaimer disclaimer, bool requireAcceptance)
        {
            return Task.FromResult(!requireAcceptance);
        }
    }

    private sealed record OptionItem(string? Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}

