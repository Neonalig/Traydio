using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Traydio.Common;

namespace Traydio.Plugin.StreamUrlLink;

public sealed class StreamUrlLinkPlugin : IRadioStationProviderPlugin
{
    private static readonly HttpClient HttpClient = new();

    public string Id => "streamurl.link";

    public string DisplayName => "StreamUrl.link";

    public async Task<IReadOnlyList<DiscoveredStation>> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(request.Query ?? string.Empty);
        var url = $"https://streamurl.link/search?q={query}";

        try
        {
            var html = await HttpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            var linkPattern = new Regex(
                "<a[^>]+href=[\"'](?<href>https?://[^\"']+)[\"'][^>]*>(?<title>[^<]+)</a>",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var results = new List<DiscoveredStation>();
            foreach (Match match in linkPattern.Matches(html))
            {
                var href = match.Groups["href"].Value;
                var title = System.Net.WebUtility.HtmlDecode(match.Groups["title"].Value.Trim());

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                if (!href.Contains("stream", StringComparison.OrdinalIgnoreCase) &&
                    !href.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) &&
                    !href.EndsWith(".pls", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(new DiscoveredStation
                {
                    Name = title,
                    StreamUrl = href,
                    Description = "Discovered via streamurl.link search",
                });
            }

            return results
                .GroupBy(r => r.StreamUrl, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(Math.Max(1, request.Limit))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}

