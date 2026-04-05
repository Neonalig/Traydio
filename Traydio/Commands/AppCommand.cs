namespace Traydio.Commands;

public sealed class AppCommand
{
    public required AppCommandKind Kind { get; init; }

    public string? StationId { get; init; }

    public int? Value { get; init; }
}

