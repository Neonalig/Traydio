using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Traydio.Common;

namespace Traydio.Plugin.FmStreamOrg;

public class FmStreamOrgPluginSettingsView : UserControl
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
        _apiKeyNameBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        _apiKeyValueBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        _apiKeyNameBox.MinWidth = 260;
        _apiKeyValueBox.MinWidth = 260;

        var root = new Grid
        {
            Margin = new Avalonia.Thickness(12),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 10,
        };

        root.Children.Add(new TextBlock
        {
            Text = "FMStream.org API Settings",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        });

        var infoText = new TextBlock
        {
            Text = "Configure API method, key parameter, and default quality preference for station discovery requests.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        Grid.SetRow(infoText, 1);
        root.Children.Add(infoText);

        var methodGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
        };
        methodGrid.Children.Add(new TextBlock { Text = "HTTP method", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        Grid.SetColumn(_apiMethodBox, 1);
        methodGrid.Children.Add(_apiMethodBox);
        Grid.SetRow(methodGrid, 2);
        root.Children.Add(methodGrid);

        var keyGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"),
            ColumnSpacing = 8,
        };
        keyGrid.Children.Add(new TextBlock { Text = "API key name", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        Grid.SetColumn(_apiKeyNameBox, 1);
        keyGrid.Children.Add(_apiKeyNameBox);
        var apiValueLabel = new TextBlock { Text = "API key value", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Grid.SetColumn(apiValueLabel, 2);
        keyGrid.Children.Add(apiValueLabel);
        Grid.SetColumn(_apiKeyValueBox, 3);
        keyGrid.Children.Add(_apiKeyValueBox);
        Grid.SetRow(keyGrid, 3);
        root.Children.Add(keyGrid);

        var saveButton = new Button
        {
            Content = "Save",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        saveButton.Click += OnSaveClick;

        var siteLinkButton = CreateHyperlinkButton("Open fmstream.org", OnOpenWebsiteClick);

        var disclaimerButton = new Button
        {
            Content = "View disclaimer information",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        disclaimerButton.Click += OnAboutConditionsClick;

        var actionPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
        };
        actionPanel.Children.Add(_defaultHighQualityBox);
        actionPanel.Children.Add(saveButton);
        Grid.SetRow(actionPanel, 4);
        root.Children.Add(actionPanel);

        siteLinkButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        Grid.SetRow(siteLinkButton, 6);
        root.Children.Add(siteLinkButton);

        var legalPanel = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };

        legalPanel.Children.Add(new TextBlock
        {
            Text = "Copyright (C) Traydio contributors. Data source: fmstream.org",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Classes = { "subtle-text" },
        });
        legalPanel.Children.Add(disclaimerButton);

        Grid.SetRow(legalPanel, 7);
        root.Children.Add(legalPanel);

        _statusText.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _statusText.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Grid.SetRow(_statusText, 8);
        root.Children.Add(_statusText);

        Content = root;
    }

    private static Button CreateHyperlinkButton(string text, EventHandler<RoutedEventArgs> clickHandler)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Foreground = Avalonia.Media.Brushes.DodgerBlue,
            TextDecorations = Avalonia.Media.TextDecorations.Underline,
        };

        var button = new Button
        {
            Content = textBlock,
            Background = Avalonia.Media.Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        button.Click += clickHandler;
        return button;
    }

    private void OnOpenWebsiteClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FmStreamOrgPlugin.InstallDisclaimer.LinkUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = FmStreamOrgPlugin.InstallDisclaimer.LinkUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _statusText.Text = "Could not open browser: " + ex.Message;
        }
    }

    private async void OnAboutConditionsClick(object? sender, RoutedEventArgs e)
    {
        var shown = await _settingsAccessor.ShowInstallDisclaimerAsync(
            FmStreamOrgPlugin.PLUGIN_ID,
            FmStreamOrgPlugin.InstallDisclaimer,
            requireAcceptance: false);
        if (!shown)
        {
            _statusText.Text = "Could not display disclaimer dialog.";
        }
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

        public Task<bool> ShowInstallDisclaimerAsync(string pluginId, PluginInstallDisclaimer disclaimer, bool requireAcceptance)
        {
            return Task.FromResult(!requireAcceptance);
        }
    }
}




