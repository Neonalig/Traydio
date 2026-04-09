using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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

    private const string _USER_AGENT = "Traydio/2026";
    private const string _OPTION_ORDER = "order";
    private const string _OPTION_REVERSE = "reverse";
    private const string _OPTION_EXACT_MATCH = "exactMatch";
    private const string _OPTION_CODEC = "codec";
    private const string _OPTION_MIN_BITRATE = "minBitrate";
    private const string _OPTION_HIDE_BROKEN = "hideBroken";
    private const int _FETCH_RETRY_ATTEMPTS = 3;

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

    private static readonly HttpClient _httpClient = CreateHttpClient();

    private static readonly StationSearchProviderFeatures _features = new()
    {
        SupportsPagination = true,
        SupportsModes = true,
        SupportsCountryFilter = true,
        SupportsGenreFilter = true,
        SupportsLanguageFilter = true,
        SupportsHighQualityPreference = true,
        SupportsOrderFilter = true,
        DefaultPageSize = 50,
        SupportedModes = [StationSearchMode.Query, StationSearchMode.Featured, StationSearchMode.Random],
    };

    private readonly ILogger<RadioBrowserPlugin> _logger;
    private readonly IPluginSettingsProvider? _settingsProvider;
    private readonly RadioBrowserApiClient _apiClient;

    public RadioBrowserPlugin(ILogger<RadioBrowserPlugin> logger, IPluginSettingsProvider? settingsProvider = null)
    {
        _logger = logger;
        _settingsProvider = settingsProvider;
        _apiClient = new RadioBrowserApiClient(_httpClient, logger);

        Capabilities =
        [
            new StationDiscoveryCapability(this),
            new StationSearchMetadataCapability(),
            new StationSearchSettingsCapability(),
            new SettingsCapability(),
        ];
    }

    public string Id => PLUGIN_ID;

    public string DisplayName => "Radio Browser";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private static string _providerId => "radio-browser.info";

    private IReadOnlyDictionary<string, string> GetSettings()
    {
        return _settingsProvider?.GetPluginSettings(PLUGIN_ID) ?? new Dictionary<string, string>();
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(12),
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _USER_AGENT);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        return client;
    }

    private async IAsyncEnumerable<DiscoveredStation> SearchStationsAsync(
        StationSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyCollection<RadioBrowserStationDto> stations;

        try
        {
            stations = await _apiClient.FetchStationsAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            yield break;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Radio Browser search failed for mode={Mode}, query={Query}", request.Mode, request.Query);
            throw;
        }

        foreach (var station in stations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var streamUrl = GetStreamUrl(station);
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
                Name = string.IsNullOrWhiteSpace(station.Name) ? "Unknown Station" : station.Name,
                StreamUrl = streamUrl!,
                Description = BuildDescription(station),
                Genre = JoinValues(SplitValues(station.Tags), maxCount: 4),
                Country = station.CountryCode,
            };
        }
    }

    private static bool IsDefaultFeaturedRequest(StationSearchRequest request)
    {
        return string.IsNullOrWhiteSpace(request.Query)
               && string.IsNullOrWhiteSpace(request.Country)
               && string.IsNullOrWhiteSpace(request.Genre)
               && string.IsNullOrWhiteSpace(request.Language);
    }

    private static IReadOnlyDictionary<string, string> BuildSearchParameters(StationSearchRequest request)
    {
        var exactMatch = GetOptionBool(request, _OPTION_EXACT_MATCH, defaultValue: false);

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hidebroken"] = GetOptionBool(request, _OPTION_HIDE_BROKEN, defaultValue: true) ? "true" : "false",
            ["offset"] = Math.Max(0, request.Offset).ToString(CultureInfo.InvariantCulture),
            ["limit"] = Math.Clamp(request.Limit, 1, 500).ToString(CultureInfo.InvariantCulture),
            ["bitrateMax"] = "1000000",
        };

        var order = ResolveOrder(request, out var reverse);
        parameters["order"] = order;
        parameters["reverse"] = reverse ? "true" : "false";

        var query = request.Query.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            parameters["name"] = query;
            parameters["nameExact"] = exactMatch ? "true" : "false";
        }

        var country = request.Country?.Trim();
        if (!string.IsNullOrWhiteSpace(country))
        {
            if (country.Length == 2)
            {
                parameters["countrycode"] = country.ToUpperInvariant();
            }
            else
            {
                parameters["country"] = country;
                parameters["countryExact"] = exactMatch ? "true" : "false";
            }
        }

        var language = request.Language?.Trim();
        if (!string.IsNullOrWhiteSpace(language))
        {
            parameters["language"] = language;
            parameters["languageExact"] = exactMatch ? "true" : "false";
        }

        var genre = request.Genre?.Trim();
        if (!string.IsNullOrWhiteSpace(genre))
        {
            parameters["tag"] = genre;
            parameters["tagExact"] = exactMatch ? "true" : "false";

            var tagList = string.Join(",", SplitValues(genre));
            if (!string.IsNullOrWhiteSpace(tagList))
            {
                parameters["tagList"] = tagList;
            }
        }

        var codec = GetOptionValue(request, _OPTION_CODEC);
        if (!string.IsNullOrWhiteSpace(codec))
        {
            parameters["codec"] = codec;
        }

        var minBitrate = GetMinimumBitrate(request);
        if (minBitrate > 0)
        {
            parameters["bitrateMin"] = minBitrate.ToString(CultureInfo.InvariantCulture);
        }

        return parameters;
    }

    private static string ResolveOrder(StationSearchRequest request, out bool reverse)
    {
        if (request.Mode == StationSearchMode.Random)
        {
            reverse = false;
            return "random";
        }

        if (request.Mode == StationSearchMode.Featured || IsDefaultFeaturedRequest(request))
        {
            reverse = true;
            return "votes";
        }

        var rawOrder = GetOptionValue(request, _OPTION_ORDER) ?? request.Order;
        if (TryResolveApiOrder(rawOrder, out var parsedOrder, out reverse))
        {
            return parsedOrder;
        }

        reverse = false;
        return "name";
    }

    private static int GetMinimumBitrate(StationSearchRequest request)
    {
        var configured = GetOptionInt(request, _OPTION_MIN_BITRATE);
        if (configured.HasValue)
        {
            return Math.Clamp(configured.Value, 0, 1_000_000);
        }

        return request.PreferHighQuality == true ? 96 : 0;
    }

    private static string? GetOptionValue(StationSearchRequest request, string key)
    {
        if (request.ProviderOptions is null || !request.ProviderOptions.TryGetValue(key, out var value))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static bool GetOptionBool(StationSearchRequest request, string key, bool defaultValue)
    {
        var raw = GetOptionValue(request, key);
        return bool.TryParse(raw, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static int? GetOptionInt(StationSearchRequest request, string key)
    {
        var raw = GetOptionValue(request, key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool TryResolveApiOrder(string? rawOrder, out string order, out bool reverse)
    {
        order = string.Empty;
        reverse = false;

        if (string.IsNullOrWhiteSpace(rawOrder))
        {
            return false;
        }

        var token = rawOrder.Trim();

        if (token.StartsWith('-'))
        {
            reverse = true;
            token = token[1..];
        }

        if (token.EndsWith("_desc", StringComparison.OrdinalIgnoreCase))
        {
            reverse = true;
            token = token[..^5];
        }
        else if (token.EndsWith("_asc", StringComparison.OrdinalIgnoreCase))
        {
            token = token[..^4];
        }

        switch (token.Trim().ToLowerInvariant())
        {
            case "top":
            case "popular":
            case "votes":
            case "vote":
            case "topvote":
                order = "votes";
                reverse = true;
                return true;

            case "name":
                order = "name";
                return true;

            case "bitrate":
                order = "bitrate";
                return true;

            case "clickcount":
            case "clicks":
            case "click":
            case "topclick":
                order = "clickcount";
                return true;

            case "country":
                order = "country";
                return true;

            case "language":
                order = "language";
                return true;

            case "codec":
                order = "codec";
                return true;

            case "random":
                order = "random";
                reverse = false;
                return true;

            default:
                return false;
        }
    }

    private static IEnumerable<string> SplitValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static v => v.Trim())
            .Where(static v => !string.IsNullOrWhiteSpace(v));
    }

    private static string? BuildDescription(RadioBrowserStationDto station)
    {
        var chunks = new List<string>();

        var languages = JoinValues(SplitValues(station.Language), maxCount: 2);
        if (!string.IsNullOrWhiteSpace(languages))
        {
            chunks.Add("Lang: " + languages);
        }

        if (station.Bitrate > 0)
        {
            chunks.Add("Bitrate: " + station.Bitrate + " kbps");
        }

        if (station.Votes > 0)
        {
            chunks.Add("Votes: " + station.Votes);
        }

        return chunks.Count == 0 ? null : string.Join(" | ", chunks);
    }

    private static string? JoinValues(IEnumerable<string>? values, int maxCount)
    {
        if (values is null)
        {
            return null;
        }

        var selected = values
            .Where(static v => !string.IsNullOrWhiteSpace(v))
            .Select(static v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToArray();

        return selected.Length == 0 ? null : string.Join(", ", selected);
    }

    private static string? GetStreamUrl(RadioBrowserStationDto station)
    {
        if (IsValidStreamUrl(station.UrlResolved))
        {
            return station.UrlResolved;
        }

        return IsValidStreamUrl(station.Url)
            ? station.Url
            : null;
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

    private sealed class RadioBrowserApiClient(HttpClient httpClient, ILogger logger)
    {
        private const string _MIRROR_SEED_HOST = "all.api.radio-browser.info";
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private static readonly SemaphoreSlim _mirrorRefreshLock = new(1, 1);

        private static string[] _cachedMirrors = [];
        private static DateTimeOffset _mirrorCacheExpiresUtc = DateTimeOffset.MinValue;

        public async Task<IReadOnlyCollection<RadioBrowserStationDto>> FetchStationsAsync(
            StationSearchRequest request,
            CancellationToken cancellationToken)
        {
            var parameters = BuildSearchParameters(request);
            Exception? lastError = null;

            for (var attempt = 1; attempt <= _FETCH_RETRY_ATTEMPTS; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var mirrors = await GetMirrorsAsync(forceRefresh: attempt > 1, cancellationToken);
                foreach (var mirror in ShuffleCopy(mirrors))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var stations = await FetchFromMirrorAsync(mirror, parameters, cancellationToken);
                        return stations;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (IsTransient(ex, cancellationToken))
                    {
                        lastError = ex;

                        logger.LogDebug(
                            ex,
                            "Radio Browser mirror failed. attempt={Attempt}/{MaxAttempts}, mirror={Mirror}, query={Query}",
                            attempt,
                            _FETCH_RETRY_ATTEMPTS,
                            mirror,
                            request.Query);

                        InvalidateMirrorCache();
                    }
                }
            }

            throw lastError ?? new HttpRequestException("Radio Browser search failed after trying all discovered mirrors.");
        }

        private async Task<IReadOnlyCollection<RadioBrowserStationDto>> FetchFromMirrorAsync(
            string mirrorHost,
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken cancellationToken)
        {
            var uri = BuildSearchUri(mirrorHost, parameters);

            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var stations = await JsonSerializer.DeserializeAsync<RadioBrowserStationDto[]>(stream, _jsonOptions, cancellationToken);

            return stations ?? [];
        }

        private static Uri BuildSearchUri(string mirrorHost, IReadOnlyDictionary<string, string> parameters)
        {
            var builder = new StringBuilder();
            var first = true;

            foreach (var (key, value) in parameters)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append('&');
                }

                first = false;
                builder.Append(Uri.EscapeDataString(key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(value));
            }

            return new Uri($"https://{mirrorHost}/json/stations/search?{builder}", UriKind.Absolute);
        }

        private static async Task<IReadOnlyList<string>> GetMirrorsAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            if (!forceRefresh &&
                _cachedMirrors.Length > 0 &&
                DateTimeOffset.UtcNow < _mirrorCacheExpiresUtc)
            {
                return _cachedMirrors;
            }

            await _mirrorRefreshLock.WaitAsync(cancellationToken);
            try
            {
                if (!forceRefresh &&
                    _cachedMirrors.Length > 0 &&
                    DateTimeOffset.UtcNow < _mirrorCacheExpiresUtc)
                {
                    return _cachedMirrors;
                }

                var discovered = await DiscoverMirrorsAsync();
                _cachedMirrors = discovered.ToArray();
                _mirrorCacheExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5);

                return _cachedMirrors;
            }
            finally
            {
                _mirrorRefreshLock.Release();
            }
        }

        private static async Task<IReadOnlyList<string>> DiscoverMirrorsAsync()
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(_MIRROR_SEED_HOST);
            }
            catch (SocketException)
            {
                return [_MIRROR_SEED_HOST];
            }

            foreach (var address in addresses)
            {
                try
                {
                    var reverse = await Dns.GetHostEntryAsync(address);
                    var host = reverse.HostName.Trim().TrimEnd('.');

                    if (string.IsNullOrWhiteSpace(host))
                    {
                        continue;
                    }

                    var forward = await Dns.GetHostAddressesAsync(host);
                    if (forward.Length > 0)
                    {
                        results.Add(host);
                    }
                }
                catch (SocketException)
                {
                    // Skip dead or non-resolvable mirrors.
                }
            }

            if (results.Count == 0)
            {
                results.Add(_MIRROR_SEED_HOST);
            }

            return results.ToArray();
        }

        private static string[] ShuffleCopy(IReadOnlyList<string> source)
        {
            var buffer = source.ToArray();

            for (var i = buffer.Length - 1; i > 0; i--)
            {
                var swapIndex = Random.Shared.Next(i + 1);
                (buffer[i], buffer[swapIndex]) = (buffer[swapIndex], buffer[i]);
            }

            return buffer;
        }

        private static bool IsTransient(Exception ex, CancellationToken cancellationToken)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (ex is TaskCanceledException)
            {
                return true;
            }

            if (ex is HttpRequestException)
            {
                return true;
            }

            return ex.InnerException is SocketException;
        }

        private static void InvalidateMirrorCache()
        {
            _mirrorCacheExpiresUtc = DateTimeOffset.MinValue;
        }
    }

    private sealed class RadioBrowserStationDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("url_resolved")]
        public string? UrlResolved { get; init; }

        [JsonPropertyName("tags")]
        public string? Tags { get; init; }

        [JsonPropertyName("countrycode")]
        public string? CountryCode { get; init; }

        [JsonPropertyName("language")]
        public string? Language { get; init; }

        [JsonPropertyName("bitrate")]
        public int Bitrate { get; init; }

        [JsonPropertyName("votes")]
        public int Votes { get; init; }
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

    private sealed class StationSearchMetadataCapability : IStationSearchProviderMetadataCapability
    {
        public string CapabilityId => "station-search-metadata";

        public string ProviderId => _providerId;

        public string WebsiteUrl => "https://www.radio-browser.info/";
    }

    private sealed class StationSearchSettingsCapability : IStationSearchSettingsCapability
    {
        public string CapabilityId => "station-search-settings";

        public string ProviderId => _providerId;

        public object CreateSearchSettingsView(IStationSearchSettingsAccessor settingsAccessor)
        {
            return new SearchSettingsView(settingsAccessor);
        }
    }

    private sealed class SearchSettingsView : UserControl
    {
        public SearchSettingsView(IStationSearchSettingsAccessor accessor)
        {
            var root = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,140,Auto,120,Auto,120,Auto,*"),
                ColumnSpacing = 8,
            };

            root.Children.Add(new TextBlock { Text = "Provider options", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });

            var orderCombo = new ComboBox
            {
                ItemsSource = new[]
                {
                    new Option("Votes", "Votes"),
                    new Option("Name", "Name"),
                    new Option("Bitrate", "Bitrate"),
                    new Option("ClickCount", "ClickCount"),
                    new Option("Random", "Random"),
                },
                SelectedValueBinding = new Avalonia.Data.Binding(nameof(Option.Value)),
                DisplayMemberBinding = new Avalonia.Data.Binding(nameof(Option.Text)),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            orderCombo.SelectedValue = accessor.GetValue(_OPTION_ORDER) ?? "Votes";
            orderCombo.SelectionChanged += (_, _) => accessor.SetValue(_OPTION_ORDER, orderCombo.SelectedValue?.ToString());
            Grid.SetColumn(orderCombo, 1);
            root.Children.Add(orderCombo);

            var reverseToggle = new CheckBox
            {
                Content = "Descending",
                IsChecked = bool.TryParse(accessor.GetValue(_OPTION_REVERSE), out var rev) ? rev : true,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            reverseToggle.IsCheckedChanged += (_, _) => accessor.SetValue(_OPTION_REVERSE, reverseToggle.IsChecked == true ? "true" : "false");
            Grid.SetColumn(reverseToggle, 2);
            root.Children.Add(reverseToggle);

            var minBitrateBox = new TextBox
            {
                Watermark = "96",
                Text = accessor.GetValue(_OPTION_MIN_BITRATE) ?? string.Empty,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            minBitrateBox.LostFocus += (_, _) => accessor.SetValue(_OPTION_MIN_BITRATE, minBitrateBox.Text);
            Grid.SetColumn(minBitrateBox, 3);
            root.Children.Add(minBitrateBox);

            var exactMatch = new CheckBox
            {
                Content = "Exact",
                IsChecked = bool.TryParse(accessor.GetValue(_OPTION_EXACT_MATCH), out var exact) && exact,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            exactMatch.IsCheckedChanged += (_, _) => accessor.SetValue(_OPTION_EXACT_MATCH, exactMatch.IsChecked == true ? "true" : "false");
            Grid.SetColumn(exactMatch, 4);
            root.Children.Add(exactMatch);

            var hideBroken = new CheckBox
            {
                Content = "Hide broken",
                IsChecked = !bool.TryParse(accessor.GetValue(_OPTION_HIDE_BROKEN), out var hide) || hide,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            hideBroken.IsCheckedChanged += (_, _) => accessor.SetValue(_OPTION_HIDE_BROKEN, hideBroken.IsChecked == true ? "true" : "false");
            Grid.SetColumn(hideBroken, 5);
            root.Children.Add(hideBroken);

            var codecBox = new TextBox
            {
                Watermark = "codec (mp3, aac)",
                Text = accessor.GetValue(_OPTION_CODEC) ?? string.Empty,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            codecBox.LostFocus += (_, _) => accessor.SetValue(_OPTION_CODEC, codecBox.Text);
            Grid.SetColumn(codecBox, 7);
            root.Children.Add(codecBox);

            Content = root;
        }

        private sealed record Option(string Value, string Text);
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
                Text = "Mirror selection is automatic. This custom endpoint field is legacy-only and is not used by the current client.",
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
            baseUrlPanel.Children.Add(new TextBlock { Text = "Legacy API Base URL (not used)" });
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