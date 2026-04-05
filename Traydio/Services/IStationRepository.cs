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

    CommunicationBridgeSettings Communication { get; }
}

