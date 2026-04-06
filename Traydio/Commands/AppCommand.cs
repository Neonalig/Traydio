namespace Traydio.Commands;

/// <summary>
/// Represents a parsed application command payload.
/// </summary>
public sealed class AppCommand
{
    /// <summary>
    /// Gets the command kind to execute.
    /// </summary>
    public required AppCommandKind Kind { get; init; }

    /// <summary>
    /// Gets the optional station id for station-specific commands.
    /// </summary>
    public string? StationId { get; init; }

    /// <summary>
    /// Gets the optional numeric value for volume-related commands.
    /// </summary>
    public int? Value { get; init; }
}

