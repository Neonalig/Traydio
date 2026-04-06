using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Traydio.Common;

namespace Traydio.Plugin.RadioBrowser;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class RadioBrowserPlugin : ITraydioPlugin
{
    public const string PLUGIN_ID = "plugin.radio-browser.info";

    public static PluginInstallDisclaimer InstallDisclaimer { get; } = new()
    {
        Version = "2026-04-06",
        Title = "Radio Browser provider information",
        Message =
            "This plugin uses community-managed radio-browser station data.\n\n" +
            "Station entries and stream links may be incorrect or stale. Verify stream legality and safety before use.",
        LinkText = "Open radio-browser.info",
        LinkUrl = "https://www.radio-browser.info/",
        AcceptButtonText = "Accept",
        RejectButtonText = "Reject",
    };

    private static readonly HttpClient _httpClient = new();
    private static readonly StationSearchProviderFeatures _features = new()
    {
        SupportsPagination = true,
        SupportsModes = true,
        SupportsCountryFilter = true,
        SupportsGenreFilter = true,
        SupportsLanguageFilter = true,
        SupportsHighQualityPreference = false,
        SupportsOrderFilter = true,
        DefaultPageSize = 50,
        SupportedModes = [StationSearchMode.Query, StationSearchMode.Featured, StationSearchMode.Random],
    };

    private readonly ILogger<RadioBrowserPlugin> _logger;
    private readonly IPluginSettingsProvider? _settingsProvider;

    public RadioBrowserPlugin(ILogger<RadioBrowserPlugin> logger, IPluginSettingsProvider? settingsProvider = null)
    {
        _logger = logger;
        _settingsProvider = settingsProvider;
        Capabilities = [new StationDiscoveryCapability(this), new SettingsCapability()];
    }

    public string Id => PLUGIN_ID;

    public string DisplayName => "Radio Browser";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private static string _providerId => "radio-browser.info";

    private IReadOnlyDictionary<string, string> GetSettings()
    {
        return _settingsProvider?.GetPluginSettings(PLUGIN_ID) ?? new Dictionary<string, string>();
    }

    private async IAsyncEnumerable<DiscoveredStation> SearchStationsAsync(
        StationSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        var requestUri = BuildRequestUri(GetBaseUrl(settings), request);
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        JsonDocument? document;
        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                yield break;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Radio Browser search failed. url={Url}", requestUri);
            yield break;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var streamUrl = FirstString(item, "url_resolved", "url");
                if (!IsValidStreamUrl(streamUrl))
                {
                    continue;
                }

                if (!seenUrls.Add(NormalizeUrl(streamUrl!)))
                {
                    continue;
                }

                yield return new DiscoveredStation
                {
                    Name = FirstString(item, "name") ?? "Unknown Station",
                    StreamUrl = streamUrl!,
                    Description = BuildDescription(FirstString(item, "language"), FirstInt(item, "bitrate")),
                    Genre = FirstString(item, "tags"),
                    Country = FirstString(item, "country"),
                };
            }
        }
    }

    private static string GetBaseUrl(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue(RadioBrowserPluginSettings.API_BASE_URL_KEY, out var configured) &&
            !string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().TrimEnd('/');
        }

        return RadioBrowserPluginSettings.DEFAULT_API_BASE_URL;
    }

    private static Uri BuildRequestUri(string baseUrl, StationSearchRequest request)
    {
        var endpoint = request.Mode switch
        {
            StationSearchMode.Featured => baseUrl + "/json/stations/topclick",
            StationSearchMode.Random => baseUrl + "/json/stations",
            _ => baseUrl + "/json/stations/search",
        };

        var queryPairs = new List<KeyValuePair<string, string>>
        {
            new("hidebroken", "true"),
            new("limit", Math.Clamp(request.Limit, 1, 500).ToString()),
            new("offset", Math.Max(0, request.Offset).ToString()),
        };

        if (request.Mode == StationSearchMode.Random)
        {
            queryPairs.Add(new("order", "random"));
        }
        else if (!string.IsNullOrWhiteSpace(request.Order))
        {
            queryPairs.Add(new("order", request.Order.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            queryPairs.Add(new("name", request.Query.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.Country))
        {
            queryPairs.Add(new("country", request.Country.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.Genre))
        {
            queryPairs.Add(new("tag", request.Genre.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            queryPairs.Add(new("language", request.Language.Trim()));
        }

        var encoded = new List<string>(queryPairs.Count);
        foreach (var pair in queryPairs)
        {
            encoded.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        }

        return new Uri(endpoint + "?" + string.Join("&", encoded));
    }

    private static string NormalizeUrl(string value)
    {
        return value.Trim().TrimEnd('/');
    }

    private static bool IsValidStreamUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static int? FirstInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) && parsed > 0)
            {
                return parsed;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out parsed) && parsed > 0)
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? BuildDescription(string? language, int? bitrate)
    {
        if (string.IsNullOrWhiteSpace(language) && bitrate is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(language))
        {
            return "Bitrate: " + bitrate + " kbps";
        }

        if (bitrate is null)
        {
            return "Language: " + language;
        }

        return "Language: " + language + ", Bitrate: " + bitrate + " kbps";
    }

    private sealed class StationDiscoveryCapability(RadioBrowserPlugin plugin) : IStationDiscoveryCapability
    {
        public string CapabilityId => "station-discovery";

        public string ProviderId => _providerId;

        public string DisplayName => plugin.DisplayName;

        public StationSearchProviderFeatures Features => _features;

        public IAsyncEnumerable<DiscoveredStation> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
        {
            return plugin.SearchStationsAsync(request, cancellationToken);
        }
    }

    private sealed class SettingsCapability : IPluginSettingsCapability
    {
        public string CapabilityId => "plugin-settings";

        public string DisplayName => "Radio Browser";

        public object CreateSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            return new RadioBrowserPluginSettingsView(settingsAccessor);
        }
    }


    private sealed class RadioBrowserPluginSettingsView : UserControl
    {
        private readonly IPluginSettingsAccessor _settingsAccessor;
        private readonly TextBox _baseUrlTextBox;
        private readonly TextBlock _statusText;

        public RadioBrowserPluginSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            _settingsAccessor = settingsAccessor;
            _statusText = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

            var root = new Grid
            {
                Margin = new Avalonia.Thickness(12),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto"),
                RowSpacing = 10,
            };

            root.Children.Add(new TextBlock
            {
                Text = "Radio Browser Provider Settings",
                FontSize = 18,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
            });

            var description = new TextBlock
            {
                Text = "Configure the Radio Browser API mirror host.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
            Grid.SetRow(description, 1);
            root.Children.Add(description);

            _baseUrlTextBox = new TextBox
            {
                Watermark = RadioBrowserPluginSettings.DEFAULT_API_BASE_URL,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            _baseUrlTextBox.Text = _settingsAccessor.GetValue(RadioBrowserPluginSettings.API_BASE_URL_KEY)
                               ?? RadioBrowserPluginSettings.DEFAULT_API_BASE_URL;
            _baseUrlTextBox.LostFocus += OnBaseUrlLostFocus;

            var baseUrlPanel = new StackPanel { Spacing = 6 };
            baseUrlPanel.Children.Add(new TextBlock { Text = "API Base URL" });
            baseUrlPanel.Children.Add(_baseUrlTextBox);
            Grid.SetRow(baseUrlPanel, 2);
            root.Children.Add(baseUrlPanel);

            var siteLinkButton = CreateHyperlinkButton("Open radio-browser.info", OnOpenWebsiteClick);
            siteLinkButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            Grid.SetRow(siteLinkButton, 4);
            root.Children.Add(siteLinkButton);

            var legalPanel = new StackPanel
            {
                Spacing = 6,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            legalPanel.Children.Add(new TextBlock
            {
                Text = "Copyright (C) Traydio contributors. Data source: radio-browser.info",
                Foreground = Avalonia.Media.Brushes.Gray,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            });

            var disclaimerButton = new Button
            {
                Content = "View disclaimer information",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            disclaimerButton.Click += OnShowDisclaimerClick;
            legalPanel.Children.Add(disclaimerButton);

            Grid.SetRow(legalPanel, 5);
            root.Children.Add(legalPanel);

            Grid.SetRow(_statusText, 6);
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

        private void OnBaseUrlLostFocus(object? sender, RoutedEventArgs e)
        {
            var value = (_baseUrlTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, RadioBrowserPluginSettings.DEFAULT_API_BASE_URL, StringComparison.OrdinalIgnoreCase))
            {
                _settingsAccessor.SetValue(RadioBrowserPluginSettings.API_BASE_URL_KEY, null);
                _baseUrlTextBox.Text = RadioBrowserPluginSettings.DEFAULT_API_BASE_URL;
                return;
            }

            _settingsAccessor.SetValue(RadioBrowserPluginSettings.API_BASE_URL_KEY, value);
        }

        private void OnOpenWebsiteClick(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InstallDisclaimer.LinkUrl))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = InstallDisclaimer.LinkUrl,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                _statusText.Text = "Could not open browser: " + ex.Message;
            }
        }

        private async void OnShowDisclaimerClick(object? sender, RoutedEventArgs e)
        {
            var shown = await _settingsAccessor.ShowInstallDisclaimerAsync(
                PLUGIN_ID,
                InstallDisclaimer,
                requireAcceptance: false);
            if (!shown)
            {
                _statusText.Text = "Could not display disclaimer dialog.";
            }
        }
    }
}

