using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Traydio.Common;

namespace Traydio.Plugin.LibVlc;

public partial class LibVlcPluginSettingsView : UserControl
{
    private readonly IPluginSettingsAccessor _settingsAccessor;
    private readonly TextBox _outputModuleBox;
    private readonly TextBox _outputDeviceIdBox;
    private readonly TextBlock _statusText;

    public LibVlcPluginSettingsView()
        : this(new NullPluginSettingsAccessor())
    {
    }

    public LibVlcPluginSettingsView(IPluginSettingsAccessor settingsAccessor)
    {
        _settingsAccessor = settingsAccessor;
        AvaloniaXamlLoader.Load(this);

        _outputModuleBox = this.FindControl<TextBox>("OutputModuleBox")
            ?? throw new InvalidOperationException("LibVLC settings view is missing OutputModuleBox.");
        _outputDeviceIdBox = this.FindControl<TextBox>("OutputDeviceIdBox")
            ?? throw new InvalidOperationException("LibVLC settings view is missing OutputDeviceIdBox.");
        _statusText = this.FindControl<TextBlock>("StatusText")
            ?? throw new InvalidOperationException("LibVLC settings view is missing StatusText.");

        _outputModuleBox.Text = _settingsAccessor.GetValue(LibVlcPluginSettings.OUTPUT_MODULE_KEY);
        _outputDeviceIdBox.Text = _settingsAccessor.GetValue(LibVlcPluginSettings.OUTPUT_DEVICE_ID_KEY);

        _outputModuleBox.TextChanged += (_, _) => UpdatePendingSettings();
        _outputDeviceIdBox.TextChanged += (_, _) => UpdatePendingSettings();
    }

    private void UpdatePendingSettings()
    {
        var module = _outputModuleBox.Text?.Trim();
        var deviceId = _outputDeviceIdBox.Text?.Trim();

        _settingsAccessor.SetValue(LibVlcPluginSettings.OUTPUT_MODULE_KEY, string.IsNullOrWhiteSpace(module) ? null : module);
        _settingsAccessor.SetValue(LibVlcPluginSettings.OUTPUT_DEVICE_ID_KEY, string.IsNullOrWhiteSpace(deviceId) ? null : deviceId);
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
}

