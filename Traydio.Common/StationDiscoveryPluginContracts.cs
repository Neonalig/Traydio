using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Traydio.Common;

public sealed class StationSearchRequest
{
    public string Query { get; init; } = string.Empty;

    public string? Country { get; init; }

    public string? Genre { get; init; }

    public int Limit { get; init; } = 100;
}

public sealed class DiscoveredStation
{
    public required string Name { get; init; }

    public required string StreamUrl { get; init; }

    public string? Description { get; init; }

    public string? Genre { get; init; }

    public string? Country { get; init; }
}

public interface IRadioStationProviderPlugin
{
    string Id { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<DiscoveredStation>> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken);
}

