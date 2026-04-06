using System.Collections.Generic;
using System.Threading;
using Traydio.Common;

namespace Traydio.Services;

public interface IStationDiscoveryService
{

    IAsyncEnumerable<DiscoveredStation> SearchAsync(
        string providerId,
        StationSearchRequest request,
        CancellationToken cancellationToken);
}

