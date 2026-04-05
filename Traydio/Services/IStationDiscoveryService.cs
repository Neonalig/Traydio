using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Traydio.Common;

namespace Traydio.Services;

public interface IStationDiscoveryService
{
    IReadOnlyList<IRadioStationProviderPlugin> GetProviders();

    Task<IReadOnlyList<DiscoveredStation>> SearchAsync(
        string providerId,
        StationSearchRequest request,
        CancellationToken cancellationToken);
}

