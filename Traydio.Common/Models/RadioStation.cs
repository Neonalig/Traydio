namespace Traydio.Models;

public sealed class RadioStation
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string StreamUrl { get; init; }
}

