using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Traydio.Common;

namespace Traydio.Plugin.FmStreamOrg;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class FmStreamOrgPlugin : ITraydioPlugin
{
    public const string PLUGIN_ID = "plugin.fmstream.org";

    private static readonly HttpClient _httpClient = new();
    private static readonly Uri _apiEndpoint = new("https://fmstream.org/index.php");
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

    public static PluginInstallDisclaimer InstallDisclaimer { get; } = new()
    {
        Version = "2026-04-06",
        Title = "FMStream.org data usage conditions",
        Message =
            "This plugin uses fmstream.org station data for non-commercial use.\n\n" +
            "By accepting, you confirm that:\n" +
            "- Traydio and this plugin are used as free/open-source software for non-commercial use only.\n" +
            "- You accept full responsibility and release fmstream.org and Traydio authors from liability.\n" +
            "- Station details and stream links may be incorrect, outdated, or legally problematic in your region.\n" +
            "- You must not republish or aggregate fmstream data into your own database.\n\n" +
            "Reject to cancel installation.",
        LinkText = "Open fmstream.org",
        LinkUrl = "https://fmstream.org/",
        AcceptButtonText = "Accept",
        RejectButtonText = "Reject",
    };

    private readonly ILogger<FmStreamOrgPlugin> _logger;
    private readonly IPluginSettingsProvider? _settingsProvider;

    public FmStreamOrgPlugin(ILogger<FmStreamOrgPlugin> logger, IPluginSettingsProvider? settingsProvider = null)
    {
        _logger = logger;
        _settingsProvider = settingsProvider;
        Capabilities = [new StationDiscoveryCapability(this), new StationSearchMetadataCapability(), new SettingsCapability(), new InstallDisclaimerCapability()];
    }

    public string Id => PLUGIN_ID;

    public string DisplayName => "FMStream.org";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private static string _providerId => "fmstream.org";
    private const int _MAX_PAGE_COUNT = 10;
    private const int _MAX_RESULT_COUNT = 500;

    private async IAsyncEnumerable<DiscoveredStation> SearchStationsAsync(
        StationSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var targetCount = Math.Clamp(request.Limit, 1, _MAX_RESULT_COUNT);
        var emitted = 0;
        var seenStreamUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        var page = 0;

        while (emitted < targetCount && page < _MAX_PAGE_COUNT)
        {
            var pageResultCount = 0;
            var newlyAdded = 0;
            await foreach (var station in SearchOfficialApiAsync(request, offset, cancellationToken))
            {
                pageResultCount++;
                cancellationToken.ThrowIfCancellationRequested();

                if (!seenStreamUrls.Add(station.StreamUrl))
                {
                    continue;
                }

                yield return station;
                emitted++;
                newlyAdded++;

                if (emitted >= targetCount)
                {
                    yield break;
                }
            }

            if (pageResultCount == 0)
            {
                yield break;
            }

            // If offset paging is ignored by the API, this prevents a duplicate-only loop.
            if (newlyAdded == 0)
            {
                yield break;
            }

            offset += pageResultCount;
            page++;
        }
    }

    private IReadOnlyDictionary<string, string> GetSettings()
    {
        return _settingsProvider?.GetPluginSettings(PLUGIN_ID) ?? new Dictionary<string, string>();
    }

    private async IAsyncEnumerable<DiscoveredStation> SearchOfficialApiAsync(
        StationSearchRequest request,
        int offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<DiscoveredStation> stations;

        try
        {
            var settings = GetSettings();
            var queryPairs = BuildApiQueryPairs(request, offset, settings);
            var usePost = IsPostConfigured(settings);

            using var requestMessage = usePost
                ? new HttpRequestMessage(HttpMethod.Post, _apiEndpoint)
                {
                    Content = new FormUrlEncodedContent(queryPairs),
                }
                : new HttpRequestMessage(HttpMethod.Get, BuildApiQueryUri(queryPairs));
            requestMessage.Headers.Accept.ParseAdd("application/json");

            using var response = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "fmstream request failed for status={StatusCode}, query={Query}, country={Country}, genre={Genre}, offset={Offset}, method={Method}",
                    response.StatusCode,
                    request.Query,
                    request.Country,
                    request.Genre,
                    offset,
                    requestMessage.Method);
                yield break;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            stations = ParseStations(doc.RootElement);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "fmstream api search failed for query={Query}, country={Country}, genre={Genre}, offset={Offset}",
                request.Query,
                request.Country,
                request.Genre,
                offset);
            yield break;
        }

        foreach (var station in stations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return station;
        }
    }

    private static bool IsPostConfigured(IReadOnlyDictionary<string, string> settings)
    {
        if (!settings.TryGetValue(FmStreamOrgPluginSettings.API_METHOD_KEY, out var method) || string.IsNullOrWhiteSpace(method))
        {
            return false;
        }

        return string.Equals(method.Trim(), "POST", StringComparison.OrdinalIgnoreCase);
    }

    private static List<KeyValuePair<string, string>> BuildApiQueryPairs(
        StationSearchRequest request,
        int offset,
        IReadOnlyDictionary<string, string> settings)
    {
        var queryPairs = new List<KeyValuePair<string, string>>();

        var country = request.Country?.Trim();
        if (!string.IsNullOrWhiteSpace(country))
        {
            queryPairs.Add(new KeyValuePair<string, string>("c", country.ToUpperInvariant()));
        }

        var genre = request.Genre?.Trim();
        if (!string.IsNullOrWhiteSpace(genre))
        {
            queryPairs.Add(new KeyValuePair<string, string>("style", genre));
        }

        var language = request.Language?.Trim();
        if (!string.IsNullOrWhiteSpace(language))
        {
            queryPairs.Add(new KeyValuePair<string, string>("l", language.ToLowerInvariant()));
        }

        var order = request.Order?.Trim();
        if (!string.IsNullOrWhiteSpace(order))
        {
            queryPairs.Add(new KeyValuePair<string, string>("o", order));
        }

        var searchText = request.Query.Trim();
        switch (request.Mode)
        {
            case StationSearchMode.Featured:
                if (string.IsNullOrWhiteSpace(country))
                {
                    queryPairs.Add(new KeyValuePair<string, string>("c", "FT"));
                }

                break;
            case StationSearchMode.Random:
                if (string.IsNullOrWhiteSpace(country))
                {
                    queryPairs.Add(new KeyValuePair<string, string>("c", "RD"));
                }

                break;
            default:
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    queryPairs.Add(new KeyValuePair<string, string>("s", searchText));
                }
                else if (string.IsNullOrWhiteSpace(country))
                {
                    queryPairs.Add(new KeyValuePair<string, string>("c", "FT"));
                }

                break;
        }

        var defaultHighQuality = true;
        if (settings.TryGetValue(FmStreamOrgPluginSettings.DEFAULT_HIGH_QUALITY_KEY, out var configuredHighQuality))
        {
            defaultHighQuality = ParseBool(configuredHighQuality, true);
        }

        var preferHighQuality = request.PreferHighQuality ?? defaultHighQuality;
        queryPairs.Add(new KeyValuePair<string, string>("hq", preferHighQuality ? "1" : "0"));

        var effectiveOffset = Math.Max(0, request.Offset + offset);
        if (effectiveOffset > 0)
        {
            queryPairs.Add(new KeyValuePair<string, string>("n", effectiveOffset.ToString()));
        }

        if (settings.TryGetValue(FmStreamOrgPluginSettings.API_KEY_VALUE_KEY, out var apiKeyValue) &&
            !string.IsNullOrWhiteSpace(apiKeyValue))
        {
            var apiKeyName = settings.TryGetValue(FmStreamOrgPluginSettings.API_KEY_NAME_KEY, out var configuredKeyName) &&
                             !string.IsNullOrWhiteSpace(configuredKeyName)
                ? configuredKeyName.Trim()
                : FmStreamOrgPluginSettings.DEFAULT_API_KEY_NAME;

            queryPairs.Add(new KeyValuePair<string, string>(apiKeyName, apiKeyValue.Trim()));
        }

        return queryPairs;
    }

    private static Uri BuildApiQueryUri(IReadOnlyList<KeyValuePair<string, string>> queryPairs)
    {
        var queryParts = new List<string>(queryPairs.Count);
        foreach (var pair in queryPairs)
        {
            queryParts.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        }

        return new UriBuilder(_apiEndpoint)
        {
            Query = string.Join("&", queryParts),
        }.Uri;
    }

    private static bool ParseBool(string value, bool defaultValue)
    {
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

    private static List<DiscoveredStation> ParseStations(JsonElement root)
    {
        var results = new List<DiscoveredStation>();
        foreach (var item in EnumerateStationElements(root))
        {
            var name = FirstString(item, "name", "program", "station", "branding", "title");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var description = FirstString(item, "description", "desc", "slogan", "info");
            var country = FirstString(item, "country", "country_name", "countryName", "nation");
            var genre = FirstString(item, "genre", "style", "format");

            foreach (var streamUrl in ExtractStreamUrls(item))
            {
                results.Add(new DiscoveredStation
                {
                    Name = name,
                    StreamUrl = streamUrl,
                    Description = description,
                    Country = country,
                    Genre = genre,
                });
            }
        }

        return results;
    }

    private static IEnumerable<JsonElement> EnumerateStationElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    yield return element;
                }
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var key in new[] { "stations", "results", "items", "data" })
        {
            if (!root.TryGetProperty(key, out var payload) || payload.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var element in payload.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    yield return element;
                }
            }

            yield break;
        }

        // Some responses may return a single station object.
        yield return root;
    }

    private static IEnumerable<string> ExtractStreamUrls(JsonElement station)
    {
        foreach (var field in new[] { "stream", "url", "stream_url", "streamUrl", "listen_url", "listenUrl" })
        {
            var value = GetString(station, field);
            if (IsValidStreamUrl(value))
            {
                yield return value!;
            }
        }

        foreach (var field in new[] { "streams", "urls" })
        {
            if (!station.TryGetProperty(field, out var streams) || streams.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in streams.EnumerateArray())
            {
                switch (entry.ValueKind)
                {
                    case JsonValueKind.String:
                    {
                        var stream = entry.GetString();
                        if (IsValidStreamUrl(stream))
                        {
                            yield return stream!;
                        }

                        break;
                    }
                    case JsonValueKind.Object:
                    {
                        var stream = FirstString(entry, "url", "stream", "link", "src");
                        if (IsValidStreamUrl(stream))
                        {
                            yield return stream!;
                        }

                        break;
                    }
                }
            }
        }
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
            var value = GetString(element, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private sealed class StationDiscoveryCapability(FmStreamOrgPlugin plugin) : IStationDiscoveryCapability
    {
        public string CapabilityId => "station-discovery";

        public StationSearchProviderFeatures Features => _features;

        public string ProviderId => _providerId;

        public string DisplayName => plugin.DisplayName;

        public IAsyncEnumerable<DiscoveredStation> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
        {
            return plugin.SearchStationsAsync(request, cancellationToken);
        }
    }

    private sealed class SettingsCapability : IPluginSettingsCapability
    {
        public string CapabilityId => "plugin-settings";

        public string DisplayName => "FMStream.org";

        public object CreateSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            return new FmStreamOrgPluginSettingsView(settingsAccessor);
        }
    }

    private sealed class StationSearchMetadataCapability : IStationSearchProviderMetadataCapability
    {
        public string CapabilityId => "station-search-metadata";

        public string ProviderId => _providerId;

        public string WebsiteUrl => "https://fmstream.org/";
    }

    private sealed class InstallDisclaimerCapability : IPluginInstallDisclaimerCapability
    {
        public string CapabilityId => "plugin-install-disclaimer";

        public PluginInstallDisclaimer Disclaimer => InstallDisclaimer;
    }

}

