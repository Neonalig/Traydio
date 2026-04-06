using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Traydio.Common;

namespace Traydio.Plugin.FmStreamOrg;

public partial class FmStreamOrgPluginSettingsView : UserControl
{
    private readonly IPluginSettingsAccessor _settingsAccessor;
    private readonly ComboBox _apiMethodBox;
    private readonly TextBox _apiKeyNameBox;
    private readonly TextBox _apiKeyValueBox;
    private readonly CheckBox _defaultHighQualityBox;
    private readonly TextBlock _statusText;

    public FmStreamOrgPluginSettingsView()
        : this(new NullPluginSettingsAccessor())
    {
    }

    public FmStreamOrgPluginSettingsView(IPluginSettingsAccessor settingsAccessor)
    {
        _settingsAccessor = settingsAccessor;

        _apiMethodBox = new ComboBox
        {
            ItemsSource = new[] { "GET", "POST" },
            SelectedIndex = 0,
            MinWidth = 140,
        };
        _apiKeyNameBox = new TextBox { Watermark = "key" };
        _apiKeyValueBox = new TextBox { Watermark = "Optional" };
        _defaultHighQualityBox = new CheckBox { Content = "Default to high quality stream order" };
        _statusText = new TextBlock();

        BuildLayout();

        var method = _settingsAccessor.GetValue(FmStreamOrgPluginSettings.API_METHOD_KEY);
        var normalizedMethod = string.Equals(method?.Trim(), "POST", StringComparison.OrdinalIgnoreCase)
            ? "POST"
            : "GET";
        _apiMethodBox.SelectedIndex = string.Equals(normalizedMethod, "POST", StringComparison.Ordinal) ? 1 : 0;

        _apiKeyNameBox.Text = _settingsAccessor.GetValue(FmStreamOrgPluginSettings.API_KEY_NAME_KEY)
            ?? FmStreamOrgPluginSettings.DEFAULT_API_KEY_NAME;
        _apiKeyValueBox.Text = _settingsAccessor.GetValue(FmStreamOrgPluginSettings.API_KEY_VALUE_KEY);

        var highQualitySetting = _settingsAccessor.GetValue(FmStreamOrgPluginSettings.DEFAULT_HIGH_QUALITY_KEY);
        _defaultHighQualityBox.IsChecked = ParseBool(highQualitySetting, defaultValue: true);
    }

    private void BuildLayout()
    {
        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(12),
            Spacing = 10,
        };

        panel.Children.Add(new TextBlock
        {
            Text = "FMStream.org API Settings",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Configure API method, key parameter, and default quality preference for station discovery requests.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        panel.Children.Add(new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "HTTP method", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                _apiMethodBox,
            },
        });

        panel.Children.Add(new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "API key name", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                _apiKeyNameBox,
                new TextBlock { Text = "API key value", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                _apiKeyValueBox,
            },
        });

        var saveButton = new Button
        {
            Content = "Save",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        saveButton.Click += OnSaveClick;

        panel.Children.Add(new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                _defaultHighQualityBox,
                saveButton,
            },
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Use POST only if your fmstream key setup requires it; default GET follows documented query parameters.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        panel.Children.Add(_statusText);

        Content = panel;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var selectedMethod = _apiMethodBox.SelectedItem?.ToString() ?? "GET";
        var keyName = _apiKeyNameBox.Text?.Trim();
        var keyValue = _apiKeyValueBox.Text?.Trim();
        var highQuality = _defaultHighQualityBox.IsChecked ?? true;

        _settingsAccessor.SetValue(
            FmStreamOrgPluginSettings.API_METHOD_KEY,
            string.Equals(selectedMethod, "POST", StringComparison.OrdinalIgnoreCase) ? "POST" : "GET");
        _settingsAccessor.SetValue(
            FmStreamOrgPluginSettings.API_KEY_NAME_KEY,
            string.IsNullOrWhiteSpace(keyName) ? FmStreamOrgPluginSettings.DEFAULT_API_KEY_NAME : keyName);
        _settingsAccessor.SetValue(
            FmStreamOrgPluginSettings.API_KEY_VALUE_KEY,
            string.IsNullOrWhiteSpace(keyValue) ? null : keyValue);
        _settingsAccessor.SetValue(FmStreamOrgPluginSettings.DEFAULT_HIGH_QUALITY_KEY, highQuality ? "1" : "0");
        _settingsAccessor.Save();

        _statusText.Text = "Saved FMStream.org settings.";
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return value.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => defaultValue,
        };
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
    }
}




