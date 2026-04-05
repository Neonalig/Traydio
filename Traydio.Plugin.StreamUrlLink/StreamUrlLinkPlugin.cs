using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Traydio.Common;

namespace Traydio.Plugin.StreamUrlLink;

public sealed class StreamUrlLinkPlugin : ITraydioPlugin
{
    private static readonly HttpClient _httpClient = new();

    public StreamUrlLinkPlugin()
    {
        Capabilities = [new StationDiscoveryCapability(this)];
    }

    public string Id => "plugin.streamurl.link";

    public string DisplayName => "StreamUrl.link";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private static string _providerId => "streamurl.link";

    private static async Task<IReadOnlyList<DiscoveredStation>> SearchStationsAsync(StationSearchRequest request, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(request.Query);
        var url = $"https://streamurl.link/search?q={query}";

        try
        {
            var html = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

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

    private sealed class StationDiscoveryCapability : IStationDiscoveryCapability
    {
        private readonly StreamUrlLinkPlugin _plugin;

        public StationDiscoveryCapability(StreamUrlLinkPlugin plugin)
        {
            _plugin = plugin;
        }

        public string CapabilityId => "station-discovery";

        public string ProviderId => _providerId;

        public string DisplayName => _plugin.DisplayName;

        public Task<IReadOnlyList<DiscoveredStation>> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
        {
            return SearchStationsAsync(request, cancellationToken);
        }
    }
}

