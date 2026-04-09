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

namespace Traydio.Plugin.Shoutcast;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class ShoutcastPlugin : ITraydioPlugin
{
    public const string PLUGIN_ID = "plugin.shoutcast.com";

    public static PluginInstallDisclaimer InstallDisclaimer { get; } = new()
    {
        Version = "2026-04-06",
        Title = "SHOUTcast provider information",
        Message =
            "This plugin accesses SHOUTcast APIs which may require an API key and may change without notice.\n\n" +
            "You are responsible for API usage terms and verifying station legality in your region.",
        LinkText = "Open SHOUTcast",
        LinkUrl = "https://www.shoutcast.com/",
        AcceptButtonText = "Accept",
        RejectButtonText = "Reject",
    };

    private static readonly HttpClient _httpClient = new();
    private static readonly StationSearchProviderFeatures _features = new()
    {
        SupportsPagination = true,
        SupportsModes = true,
        SupportsCountryFilter = false,
        SupportsGenreFilter = true,
        SupportsLanguageFilter = false,
        SupportsHighQualityPreference = false,
        SupportsOrderFilter = false,
        DefaultPageSize = 50,
        SupportedModes = [StationSearchMode.Query, StationSearchMode.Featured],
    };

    private readonly ILogger<ShoutcastPlugin> _logger;
    private readonly IPluginSettingsProvider? _settingsProvider;

    public ShoutcastPlugin(ILogger<ShoutcastPlugin> logger, IPluginSettingsProvider? settingsProvider = null)
    {
        _logger = logger;
        _settingsProvider = settingsProvider;
        Capabilities = [new StationDiscoveryCapability(this), new StationSearchMetadataCapability(), new SettingsCapability()];
    }

    public string Id => PLUGIN_ID;

    public string DisplayName => "SHOUTcast";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private static string _providerId => "shoutcast.com";

    private IReadOnlyDictionary<string, string> GetSettings()
    {
        return _settingsProvider?.GetPluginSettings(PLUGIN_ID) ?? new Dictionary<string, string>();
    }

    private async IAsyncEnumerable<DiscoveredStation> SearchStationsAsync(
        StationSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        var apiKey = GetApiKey(settings);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Shoutcast typically requires a key; fail quietly so UI remains responsive.
            yield break;
        }

        var requestUri = BuildRequestUri(settings, request, apiKey);
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        JsonDocument document;
        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                yield break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Shoutcast search failed. url={Url}", requestUri);
            yield break;
        }

        using (document)
        {
            foreach (var item in EnumerateStationItems(document.RootElement))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stationId = FirstString(item, "id", "stationid");
                if (string.IsNullOrWhiteSpace(stationId))
                {
                    continue;
                }

                var streamUrl = "https://yp.shoutcast.com/sbin/tunein-station.m3u?id=" + Uri.EscapeDataString(stationId);
                var normalizedUrl = NormalizeUrl(streamUrl);
                if (!seenUrls.Add(normalizedUrl))
                {
                    continue;
                }

                var name = FirstString(item, "name") ?? "Unknown Station";
                var genre = FirstString(item, "genre");
                var bitrate = FirstInt(item, "br", "bitrate");

                yield return new DiscoveredStation
                {
                    Name = name,
                    StreamUrl = streamUrl,
                    Description = bitrate is > 0 ? "Bitrate: " + bitrate + " kbps" : null,
                    Genre = genre,
                    Country = null,
                };
            }
        }
    }

    private static Uri BuildRequestUri(IReadOnlyDictionary<string, string> settings, StationSearchRequest request, string apiKey)
    {
        var baseUrl = GetBaseUrl(settings);
        var endpoint = request.Mode switch
        {
            StationSearchMode.Featured => baseUrl + "/legacy/Top500",
            _ => baseUrl + "/legacy/stationsearch",
        };

        var queryPairs = new List<KeyValuePair<string, string>>
        {
            new("k", apiKey),
            new("f", "json"),
            new("limit", Math.Clamp(request.Limit, 1, 500).ToString()),
            new("offset", Math.Max(0, request.Offset).ToString()),
        };

        if (request.Mode != StationSearchMode.Featured && !string.IsNullOrWhiteSpace(request.Query))
        {
            queryPairs.Add(new("search", request.Query.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.Genre))
        {
            queryPairs.Add(new("genre", request.Genre.Trim()));
        }

        var queryParts = new List<string>(queryPairs.Count);
        foreach (var pair in queryPairs)
        {
            queryParts.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        }

        return new Uri(endpoint + "?" + string.Join("&", queryParts));
    }

    private static string GetBaseUrl(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue(ShoutcastPluginSettings.API_BASE_URL_KEY, out var configured) &&
            !string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().TrimEnd('/');
        }

        return ShoutcastPluginSettings.DEFAULT_API_BASE_URL;
    }

    private static string? GetApiKey(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue(ShoutcastPluginSettings.API_KEY_KEY, out var apiKey) &&
            !string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey.Trim();
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateStationItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (root.TryGetProperty("stationlist", out var stationList) &&
            stationList.ValueKind == JsonValueKind.Object &&
            stationList.TryGetProperty("station", out var stationArray) &&
            stationArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in stationArray.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (root.TryGetProperty("response", out var response) &&
            response.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in EnumerateStationItems(response))
            {
                yield return item;
            }
        }
    }

    private static int? FirstInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue) && intValue > 0)
            {
                return intValue;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out intValue) && intValue > 0)
            {
                return intValue;
            }
        }

        return null;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                var text = value.GetRawText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string NormalizeUrl(string value)
    {
        return value.Trim().TrimEnd('/');
    }

    private sealed class StationDiscoveryCapability(ShoutcastPlugin plugin) : IStationDiscoveryCapability
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

        public string DisplayName => "SHOUTcast";

        public object CreateSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            return new ShoutcastPluginSettingsView(settingsAccessor);
        }
    }

    private sealed class StationSearchMetadataCapability : IStationSearchProviderMetadataCapability
    {
        public string CapabilityId => "station-search-metadata";

        public string ProviderId => _providerId;

        public string WebsiteUrl => "https://www.shoutcast.com/";
    }


    private sealed class ShoutcastPluginSettingsView : UserControl
    {
        private readonly IPluginSettingsAccessor _settingsAccessor;
        private readonly TextBox _apiBaseUrlTextBox;
        private readonly TextBox _apiKeyTextBox;
        private readonly TextBlock _statusText;

        public ShoutcastPluginSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            _settingsAccessor = settingsAccessor;
            _statusText = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

            var root = new Grid
            {
                Margin = new Avalonia.Thickness(12),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto,Auto"),
                RowSpacing = 10,
            };

            root.Children.Add(new TextBlock
            {
                Text = "SHOUTcast Provider Settings",
                FontSize = 18,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
            });

            var description = new TextBlock
            {
                Text = "Set your SHOUTcast API key and optional API base URL.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
            Grid.SetRow(description, 1);
            root.Children.Add(description);

            _apiKeyTextBox = new TextBox
            {
                Watermark = "API key",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Text = _settingsAccessor.GetValue(ShoutcastPluginSettings.API_KEY_KEY) ?? string.Empty
            };
            _apiKeyTextBox.LostFocus += OnApiKeyLostFocus;

            var keyPanel = new StackPanel { Spacing = 6 };
            keyPanel.Children.Add(new TextBlock { Text = "API Key" });
            keyPanel.Children.Add(_apiKeyTextBox);
            Grid.SetRow(keyPanel, 2);
            root.Children.Add(keyPanel);

            _apiBaseUrlTextBox = new TextBox
            {
                Watermark = ShoutcastPluginSettings.DEFAULT_API_BASE_URL,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Text = _settingsAccessor.GetValue(ShoutcastPluginSettings.API_BASE_URL_KEY)
                    ?? ShoutcastPluginSettings.DEFAULT_API_BASE_URL
            };
            _apiBaseUrlTextBox.LostFocus += OnApiBaseUrlLostFocus;

            var baseUrlPanel = new StackPanel { Spacing = 6 };
            baseUrlPanel.Children.Add(new TextBlock { Text = "API Base URL" });
            baseUrlPanel.Children.Add(_apiBaseUrlTextBox);
            Grid.SetRow(baseUrlPanel, 3);
            root.Children.Add(baseUrlPanel);

            var siteLinkButton = CreateHyperlinkButton("Open shoutcast.com", OnOpenWebsiteClick);
            siteLinkButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            Grid.SetRow(siteLinkButton, 5);
            root.Children.Add(siteLinkButton);

            var legalPanel = new StackPanel
            {
                Spacing = 6,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            legalPanel.Children.Add(new TextBlock
            {
                Text = "Copyright (C) Traydio contributors. Data source: shoutcast.com",
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

            Grid.SetRow(legalPanel, 6);
            root.Children.Add(legalPanel);

            Grid.SetRow(_statusText, 7);
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

        private void OnApiKeyLostFocus(object? sender, RoutedEventArgs e)
        {
            var apiKey = (_apiKeyTextBox.Text ?? string.Empty).Trim();
            _settingsAccessor.SetValue(ShoutcastPluginSettings.API_KEY_KEY, string.IsNullOrWhiteSpace(apiKey) ? null : apiKey);
        }

        private void OnApiBaseUrlLostFocus(object? sender, RoutedEventArgs e)
        {
            var value = (_apiBaseUrlTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, ShoutcastPluginSettings.DEFAULT_API_BASE_URL, StringComparison.OrdinalIgnoreCase))
            {
                _settingsAccessor.SetValue(ShoutcastPluginSettings.API_BASE_URL_KEY, null);
                _apiBaseUrlTextBox.Text = ShoutcastPluginSettings.DEFAULT_API_BASE_URL;
                return;
            }

            _settingsAccessor.SetValue(ShoutcastPluginSettings.API_BASE_URL_KEY, value);
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

