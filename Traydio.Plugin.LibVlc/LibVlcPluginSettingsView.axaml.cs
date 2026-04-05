using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Traydio.Common;

namespace Traydio.Plugin.LibVlc;

public partial class LibVlcPluginSettingsView : UserControl
{
    private readonly IPluginSettingsAccessor _settingsAccessor;

    public LibVlcPluginSettingsView(IPluginSettingsAccessor settingsAccessor)
    {
        _settingsAccessor = settingsAccessor;
        AvaloniaXamlLoader.Load(this);

        OutputModuleBox.Text = _settingsAccessor.GetValue(LibVlcPluginSettings.OutputModuleKey);
        OutputDeviceIdBox.Text = _settingsAccessor.GetValue(LibVlcPluginSettings.OutputDeviceIdKey);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var module = OutputModuleBox.Text?.Trim();
        var deviceId = OutputDeviceIdBox.Text?.Trim();

        _settingsAccessor.SetValue(LibVlcPluginSettings.OutputModuleKey, string.IsNullOrWhiteSpace(module) ? null : module);
        _settingsAccessor.SetValue(LibVlcPluginSettings.OutputDeviceIdKey, string.IsNullOrWhiteSpace(deviceId) ? null : deviceId);
        _settingsAccessor.Save();

        StatusText.Text = "Saved LibVLC output settings. Restart playback to apply changes.";
    }
}

