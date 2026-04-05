using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Traydio.Common;

public interface IPluginCapability
{
    string CapabilityId { get; }
}

public interface IStationDiscoveryCapability : IPluginCapability
{
    string ProviderId { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<DiscoveredStation>> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken);
}

public interface ITraydioPlugin
{
    string Id { get; }

    string DisplayName { get; }

    IReadOnlyList<IPluginCapability> Capabilities { get; }
}

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

