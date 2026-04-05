using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Traydio.Common;

namespace Traydio.Plugin.FmStreamOrg;

public sealed class FmStreamOrgPlugin : IRadioStationProviderPlugin
{
    private static readonly HttpClient HttpClient = new();

    public string Id => "fmstream.org";

    public string DisplayName => "FMStream.org";

    public async Task<IReadOnlyList<DiscoveredStation>> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(request.Query ?? string.Empty);
        var stations = await TryJsonApiAsync(query, cancellationToken).ConfigureAwait(false);
        if (stations.Count > 0)
        {
            return stations;
        }

        return await TryHtmlFallbackAsync(query, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<DiscoveredStation>> TryJsonApiAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync($"https://fmstream.org/api/search?q={query}", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
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

    private static async Task<IReadOnlyList<DiscoveredStation>> TryHtmlFallbackAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var html = await HttpClient.GetStringAsync($"https://fmstream.org/search/{query}", cancellationToken).ConfigureAwait(false);

            var urlPattern = new Regex("https?://[^\"'\\s<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var titlePattern = new Regex("<h[1-6][^>]*>(?<t>[^<]+)</h[1-6]>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var urls = urlPattern.Matches(html)
                .Select(m => m.Value)
                .Where(u => u.Contains("stream", StringComparison.OrdinalIgnoreCase) || u.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || u.EndsWith(".pls", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToArray();

            var titles = titlePattern.Matches(html)
                .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups["t"].Value.Trim()))
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
}

