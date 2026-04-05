using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traydio.Common;

namespace Traydio.Services.Implementations;

public sealed class StationDiscoveryService : IStationDiscoveryService
{
    private readonly IPluginManager _pluginManager;

    public StationDiscoveryService(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    public IReadOnlyList<IRadioStationProviderPlugin> GetProviders()
    {
        return _pluginManager.GetPlugins()
            .SelectMany(p => p.Capabilities.OfType<IStationDiscoveryCapability>())
            .Select(c => (IRadioStationProviderPlugin)new CapabilityBackedProvider(c))
            .ToArray();
    }

    public async Task<IReadOnlyList<DiscoveredStation>> SearchAsync(
        string providerId,
        StationSearchRequest request,
        CancellationToken cancellationToken)
    {
        var provider = GetProviders()
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

    private sealed class CapabilityBackedProvider : IRadioStationProviderPlugin
    {
        private readonly IStationDiscoveryCapability _capability;

        public CapabilityBackedProvider(IStationDiscoveryCapability capability)
        {
            _capability = capability;
        }

        public string Id => _capability.ProviderId;

        public string DisplayName => _capability.DisplayName;

        public Task<IReadOnlyList<DiscoveredStation>> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
        {
            return _capability.SearchAsync(request, cancellationToken);
        }
    }
}

