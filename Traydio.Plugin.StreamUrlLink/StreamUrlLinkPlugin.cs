using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Traydio.Common;

namespace Traydio.Plugin.StreamUrlLink;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed partial class StreamUrlLinkPlugin : ITraydioPlugin
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

    private static async IAsyncEnumerable<DiscoveredStation> SearchStationsAsync(
        StationSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(request.Query);
        var url = $"https://streamurl.link/search?q={query}";

        DiscoveredStation[] stations;
        try
        {
            var html = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            var linkPattern = LinkPattern();

            var results = new List<DiscoveredStation>();
            foreach (Match match in linkPattern.Matches(html))
            {
                var href = match.Groups["href"].Value;
                var title = WebUtility.HtmlDecode(match.Groups["title"].Value.Trim());

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

            stations = results
                .GroupBy(r => r.StreamUrl, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(Math.Max(1, request.Limit))
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var station in stations)
        {
            yield return station;
        }
    }

    private sealed class StationDiscoveryCapability(StreamUrlLinkPlugin plugin) : IStationDiscoveryCapability
    {
        public string CapabilityId => "station-discovery";

        public string ProviderId => _providerId;

        public string DisplayName => plugin.DisplayName;

        public IAsyncEnumerable<DiscoveredStation> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
        {
            return SearchStationsAsync(request, cancellationToken);
        }
    }

    [GeneratedRegex("<a[^>]+href=[\"'](?<href>https?://[^\"']+)[\"'][^>]*>(?<title>[^<]+)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}

