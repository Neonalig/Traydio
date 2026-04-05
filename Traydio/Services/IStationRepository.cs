using System;
using System.Collections.Generic;
using Traydio.Models;

namespace Traydio.Services;

public interface IStationRepository
{
    event EventHandler? Changed;

    IReadOnlyList<RadioStation> GetStations();

    RadioStation? GetStation(string stationId);

    RadioStation AddStation(string name, string streamUrl);

    bool RemoveStation(string stationId);

    string? ActiveStationId { get; set; }

    int Volume { get; set; }

    string? RadioPlayerEngineId { get; set; }

    string? AudioOutputDeviceId { get; set; }

    CommunicationBridgeSettings Communication { get; }

    StationDiscoveryPluginSettings StationDiscoveryPlugins { get; }

    void SaveCommunicationSettings(CommunicationBridgeSettings settings);

    void SaveStationDiscoveryPluginSettings(StationDiscoveryPluginSettings settings);

    IReadOnlyDictionary<string, string> GetPluginSettings(string pluginId);

    void SavePluginSettings(string pluginId, IReadOnlyDictionary<string, string> settings);
}

