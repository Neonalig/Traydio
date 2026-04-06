namespace Traydio.Common;

public interface IPluginCapability
{
    string CapabilityId { get; }
}

public interface IStationDiscoveryCapability : IPluginCapability
{
    string ProviderId { get; }

    string DisplayName { get; }

    StationSearchProviderFeatures Features { get; }

    IAsyncEnumerable<DiscoveredStation> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken);
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

    public StationSearchMode Mode { get; init; } = StationSearchMode.Query;

    public string? Country { get; init; }

    public string? Genre { get; init; }

    public string? Language { get; init; }

    public string? Order { get; init; }

    public bool? PreferHighQuality { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = 100;
}

public enum StationSearchMode
{
    Query,
    Featured,
    Random,
}

public sealed class StationSearchProviderFeatures
{
    public static StationSearchProviderFeatures Basic { get; } = new();

    public bool SupportsPagination { get; init; }

    public bool SupportsModes { get; init; }

    public bool SupportsCountryFilter { get; init; }

    public bool SupportsGenreFilter { get; init; }

    public bool SupportsLanguageFilter { get; init; }

    public bool SupportsHighQualityPreference { get; init; }

    public bool SupportsOrderFilter { get; init; }

    public int DefaultPageSize { get; init; } = 100;

    public IReadOnlyList<StationSearchMode> SupportedModes { get; init; } = [StationSearchMode.Query];
}

public sealed class DiscoveredStation
{
    public required string Name { get; init; }

    public required string StreamUrl { get; init; }

    public string? Description { get; init; }

    public string? Genre { get; init; }

    public string? Country { get; init; }
}


