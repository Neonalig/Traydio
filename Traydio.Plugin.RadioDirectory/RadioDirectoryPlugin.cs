using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Traydio.Common;

namespace Traydio.Plugin.RadioDirectory;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class RadioDirectoryPlugin : ITraydioPlugin
{
    public const string PLUGIN_ID = "plugin.radio-directory.com";

    public static PluginInstallDisclaimer InstallDisclaimer { get; } = new()
    {
        Version = "2026-04-06",
        Title = "Radio Directory provider information",
        Message =
            "This plugin depends on an external radio directory API that may change schema or availability without notice.\n\n" +
            "Please verify stream legality and reliability before use.",
        LinkText = "Open radio-directory.com",
        LinkUrl = "https://radio-directory.com/",
        AcceptButtonText = "Accept",
        RejectButtonText = "Reject",
    };

    private static readonly HttpClient _httpClient = new();
    private static readonly StationSearchProviderFeatures _features = new()
    {
        SupportsPagination = true,
        SupportsModes = false,
        SupportsCountryFilter = false,
        SupportsGenreFilter = true,
        SupportsLanguageFilter = false,
        SupportsHighQualityPreference = false,
        SupportsOrderFilter = false,
        DefaultPageSize = 50,
        SupportedModes = [StationSearchMode.Query],
    };

    private readonly ILogger<RadioDirectoryPlugin> _logger;
    private readonly IPluginSettingsProvider? _settingsProvider;

    public RadioDirectoryPlugin(ILogger<RadioDirectoryPlugin> logger, IPluginSettingsProvider? settingsProvider = null)
    {
        _logger = logger;
        _settingsProvider = settingsProvider;
        Capabilities = [new StationDiscoveryCapability(this), new StationSearchMetadataCapability(), new SettingsCapability()];
    }

    public string Id => PLUGIN_ID;

    public string DisplayName => "Radio Directory";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private static string _providerId => "radio-directory.com";

    private IReadOnlyDictionary<string, string> GetSettings()
    {
        return _settingsProvider?.GetPluginSettings(PLUGIN_ID) ?? new Dictionary<string, string>();
    }

    private async IAsyncEnumerable<DiscoveredStation> SearchStationsAsync(
        StationSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        var requestUri = BuildRequestUri(settings, request);
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
            _logger.LogDebug(ex, "Radio Directory search failed. url={Url}", requestUri);
            yield break;
        }

        using (document)
        {
            foreach (var item in EnumerateStationItems(document.RootElement))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var streamUrl = FirstString(item, "stream_url", "streamUrl", "url");
                if (!IsValidStreamUrl(streamUrl))
                {
                    continue;
                }

                var normalized = NormalizeUrl(streamUrl!);
                if (!seenUrls.Add(normalized))
                {
                    continue;
                }

                yield return new DiscoveredStation
                {
                    Name = FirstString(item, "title", "name") ?? "Unknown Station",
                    StreamUrl = streamUrl!,
                    Description = FirstString(item, "description", "summary"),
                    Genre = FirstString(item, "genre", "style"),
                    Country = FirstString(item, "country"),
                };
            }
        }
    }

    private static Uri BuildRequestUri(IReadOnlyDictionary<string, string> settings, StationSearchRequest request)
    {
        var baseUrl = GetBaseUrl(settings);
        var pageSize = Math.Clamp(request.Limit, 1, 200);
        var page = (Math.Max(0, request.Offset) / pageSize) + 1;

        var queryPairs = new List<KeyValuePair<string, string>>
        {
            new("limit", pageSize.ToString()),
            new("page", page.ToString()),
        };

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            queryPairs.Add(new("q", request.Query.Trim()));
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

        return new Uri(baseUrl + "?" + string.Join("&", queryParts));
    }

    private static string GetBaseUrl(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue(RadioDirectoryPluginSettings.API_BASE_URL_KEY, out var configured) &&
            !string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().TrimEnd('/');
        }

        return RadioDirectoryPluginSettings.DEFAULT_API_BASE_URL;
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

        foreach (var key in new[] { "data", "results", "items", "stations" })
        {
            if (!root.TryGetProperty(key, out var payload) || payload.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in payload.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }
    }

    private static bool IsValidStreamUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
               (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }

    private static string NormalizeUrl(string value)
    {
        return value.Trim().TrimEnd('/');
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
        }

        return null;
    }

    private sealed class StationDiscoveryCapability(RadioDirectoryPlugin plugin) : IStationDiscoveryCapability
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

        public string DisplayName => "Radio Directory";

        public object CreateSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            return new RadioDirectoryPluginSettingsView(settingsAccessor);
        }
    }

    private sealed class StationSearchMetadataCapability : IStationSearchProviderMetadataCapability
    {
        public string CapabilityId => "station-search-metadata";

        public string ProviderId => _providerId;

        public string WebsiteUrl => "https://radio-directory.com/";
    }


    private sealed class RadioDirectoryPluginSettingsView : UserControl
    {
        private readonly IPluginSettingsAccessor _settingsAccessor;
        private readonly TextBox _baseUrlTextBox;
        private readonly TextBlock _statusText;

        public RadioDirectoryPluginSettingsView(IPluginSettingsAccessor settingsAccessor)
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
                Text = "Radio Directory Provider Settings",
                FontSize = 18,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
            });

            var description = new TextBlock
            {
                Text = "Configure the Radio Directory search API endpoint.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
            Grid.SetRow(description, 1);
            root.Children.Add(description);

            _baseUrlTextBox = new TextBox
            {
                Watermark = RadioDirectoryPluginSettings.DEFAULT_API_BASE_URL,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Text = _settingsAccessor.GetValue(RadioDirectoryPluginSettings.API_BASE_URL_KEY)
                    ?? RadioDirectoryPluginSettings.DEFAULT_API_BASE_URL
            };
            _baseUrlTextBox.LostFocus += OnBaseUrlLostFocus;

            var baseUrlPanel = new StackPanel { Spacing = 6 };
            baseUrlPanel.Children.Add(new TextBlock { Text = "API Search URL" });
            baseUrlPanel.Children.Add(_baseUrlTextBox);
            Grid.SetRow(baseUrlPanel, 2);
            root.Children.Add(baseUrlPanel);

            var siteLinkButton = CreateHyperlinkButton("Open radio-directory.com", OnOpenWebsiteClick);
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
                Text = "Copyright (C) Traydio contributors. Data source: radio-directory.com",
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
                string.Equals(value, RadioDirectoryPluginSettings.DEFAULT_API_BASE_URL, StringComparison.OrdinalIgnoreCase))
            {
                _settingsAccessor.SetValue(RadioDirectoryPluginSettings.API_BASE_URL_KEY, null);
                _baseUrlTextBox.Text = RadioDirectoryPluginSettings.DEFAULT_API_BASE_URL;
                return;
            }

            _settingsAccessor.SetValue(RadioDirectoryPluginSettings.API_BASE_URL_KEY, value);
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

        private void OnShowDisclaimerClick(object? sender, RoutedEventArgs e)
        {
            _ = ShowDisclaimerAsync();
        }

        private async Task ShowDisclaimerAsync()
        {
            try
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
            catch (Exception ex)
            {
                _statusText.Text = "Could not display disclaimer dialog: " + ex.Message;
            }
        }
    }
}

