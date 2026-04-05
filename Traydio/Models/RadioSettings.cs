using System.Collections.Generic;

namespace Traydio.Models;

public sealed class RadioSettings
{
    public List<RadioStation> Stations { get; set; } = new();

    public string? ActiveStationId { get; set; }

    public int Volume { get; set; } = 60;
}

