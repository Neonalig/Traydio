using System.Collections.Generic;

namespace Traydio.Models;

public sealed class StationDiscoveryPluginSettings
{
    public string PluginDirectory { get; set; } = "Plugins";

    public List<string> DisabledPluginIds { get; set; } = [];
}

public sealed class CommunicationBridgeSettings
{
    public bool EnableNamedPipeRelay { get; set; } = true;

    public bool EnableLoopbackRelay { get; set; }

    public string LoopbackHost { get; set; } = "127.0.0.1";

    public int LoopbackPort { get; set; } = 38473;

    public bool EnableProtocolUrlRelay { get; set; } = true;

    public string ProtocolScheme { get; set; } = "traydio";
}

public sealed class RadioSettings
{
    public List<RadioStation> Stations { get; set; } = [];

    public string? ActiveStationId { get; set; }

    public int Volume { get; set; } = 60;

    public string? AudioOutputDeviceId { get; set; }

    public CommunicationBridgeSettings Communication { get; set; } = new();

    public StationDiscoveryPluginSettings StationDiscoveryPlugins { get; set; } = new();
}

