using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Traydio.Common;

namespace Traydio.Services.Implementations;

public sealed class StationDiscoveryService(IPluginManager pluginManager) : IStationDiscoveryService
{
    public async IAsyncEnumerable<DiscoveredStation> SearchAsync(
        string providerId,
        StationSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var capability = pluginManager.GetPlugins()
            .SelectMany(plugin => plugin.Capabilities.OfType<IStationDiscoveryCapability>())
            .FirstOrDefault(c => string.Equals(c.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

        if (capability is null)
        {
            yield break;
        }

        var maxCount = Math.Max(1, request.Limit);
        var yielded = 0;

        await foreach (var result in capability.SearchAsync(request, cancellationToken))
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
}

