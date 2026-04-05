using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traydio.Common;

namespace Traydio.Services.Implementations;

public sealed class StationDiscoveryService : IStationDiscoveryService
{
    private readonly IStationDiscoveryPluginManager _pluginManager;

    public StationDiscoveryService(IStationDiscoveryPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    public IReadOnlyList<IRadioStationProviderPlugin> GetProviders()
    {
        return _pluginManager.GetProviders();
    }

    public async Task<IReadOnlyList<DiscoveredStation>> SearchAsync(
        string providerId,
        StationSearchRequest request,
        CancellationToken cancellationToken)
    {
        var provider = _pluginManager.GetProviders()
            .FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            return [];
        }

        var results = await provider.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        return results
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && !string.IsNullOrWhiteSpace(r.StreamUrl))
            .Take(Math.Max(1, request.Limit))
            .ToArray();
    }
}

