using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Traydio.Common;

namespace Traydio.Plugin.FmStreamOrg;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed partial class FmStreamOrgPlugin : ITraydioPlugin
{
    private static readonly HttpClient _httpClient = new();

    public FmStreamOrgPlugin()
    {
        Capabilities = [new StationDiscoveryCapability(this)];
    }

    public string Id => "plugin.fmstream.org";

    public string DisplayName => "FMStream.org";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private static string _providerId => "fmstream.org";

    private static async IAsyncEnumerable<DiscoveredStation> SearchStationsAsync(
        StationSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(request.Query);

        var jsonResults = await TryJsonApiAsync(query, cancellationToken).ConfigureAwait(false);
        if (jsonResults.Count > 0)
        {
            foreach (var station in jsonResults)
            {
                yield return station;
            }

            yield break;
        }

        var fallbackResults = await TryHtmlFallbackAsync(query, cancellationToken).ConfigureAwait(false);
        foreach (var station in fallbackResults)
        {
            yield return station;
        }
    }

    private static async Task<List<DiscoveredStation>> TryJsonApiAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"https://fmstream.org/api/search?q={query}", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<DiscoveredStation>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var streamUrl = GetString(item, "stream") ?? GetString(item, "url");
                var name = GetString(item, "name");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(streamUrl))
                {
                    continue;
                }

                results.Add(new DiscoveredStation
                {
                    Name = name,
                    StreamUrl = streamUrl,
                    Description = GetString(item, "description"),
                    Country = GetString(item, "country"),
                    Genre = GetString(item, "genre"),
                });
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    private static async Task<List<DiscoveredStation>> TryHtmlFallbackAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var html = await _httpClient.GetStringAsync($"https://fmstream.org/search/{query}", cancellationToken).ConfigureAwait(false);

            var urlPattern = UrlPattern();
            var titlePattern = TitlePattern();

            var urls = urlPattern.Matches(html)
                .Select(m => m.Value)
                .Where(u => u.Contains("stream", StringComparison.OrdinalIgnoreCase) || u.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || u.EndsWith(".pls", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToArray();

            var titles = titlePattern.Matches(html)
                .Select(m => WebUtility.HtmlDecode(m.Groups["t"].Value.Trim()))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToArray();

            var results = new List<DiscoveredStation>();
            for (var i = 0; i < urls.Length; i++)
            {
                var title = i < titles.Length ? titles[i] : $"FMStream Station {i + 1}";
                results.Add(new DiscoveredStation
                {
                    Name = title,
                    StreamUrl = urls[i],
                    Description = "Discovered via fmstream.org HTML search",
                });
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private sealed class StationDiscoveryCapability(FmStreamOrgPlugin plugin) : IStationDiscoveryCapability
    {
        public string CapabilityId => "station-discovery";

        public string ProviderId => _providerId;

        public string DisplayName => plugin.DisplayName;

        public IAsyncEnumerable<DiscoveredStation> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
        {
            return SearchStationsAsync(request, cancellationToken);
        }
    }

    [GeneratedRegex("https?://[^\"'\\s<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex("<h[1-6][^>]*>(?<t>[^<]+)</h[1-6]>", RegexOptions.IgnoreCase)]
    private static partial Regex TitlePattern();
}

