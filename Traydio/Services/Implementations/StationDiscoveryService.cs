using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Traydio.Common;

namespace Traydio.Services.Implementations;

public sealed class StationDiscoveryService(IPluginManager pluginManager) : IStationDiscoveryService
{
    public IReadOnlyList<IRadioStationProviderPlugin> GetProviders()
    {
        return pluginManager.GetPlugins()
            .SelectMany(p => p.Capabilities.OfType<IStationDiscoveryCapability>())
            .Select(IRadioStationProviderPlugin (c) => new CapabilityBackedProvider(c))
            .ToArray();
    }

    public async IAsyncEnumerable<DiscoveredStation> SearchAsync(
        string providerId,
        StationSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var provider = GetProviders()
            .FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            yield break;
        }

        var maxCount = Math.Max(1, request.Limit);
        var yielded = 0;

        await foreach (var result in provider.SearchAsync(request, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(result.Name) || string.IsNullOrWhiteSpace(result.StreamUrl))
            {
                continue;
            }

            yield return result;
            yielded++;

            if (yielded >= maxCount)
            {
                yield break;
            }
        }
    }

    private sealed class CapabilityBackedProvider(IStationDiscoveryCapability capability) : IRadioStationProviderPlugin
    {
        public string Id => capability.ProviderId;

        public string DisplayName => capability.DisplayName;

        public IAsyncEnumerable<DiscoveredStation> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
        {
            return capability.SearchAsync(request, cancellationToken);
        }
    }
}

