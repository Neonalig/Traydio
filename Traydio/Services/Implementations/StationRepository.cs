using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Traydio.Models;

namespace Traydio.Services;

public sealed class StationRepository : IStationRepository
{
    private readonly string _settingsPath;
    private readonly RadioSettings _settings;

    public event EventHandler? Changed;

    public StationRepository()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Traydio");

        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");
        _settings = LoadSettings(_settingsPath);
    }

    public IReadOnlyList<RadioStation> GetStations()
    {
        return _settings.Stations;
    }

    public RadioStation? GetStation(string stationId)
    {
        return _settings.Stations.FirstOrDefault(s => string.Equals(s.Id, stationId, StringComparison.Ordinal));
    }

    public RadioStation AddStation(string name, string streamUrl)
    {
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Station URL must be an absolute URI.");
        }

        var station = new RadioStation
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            StreamUrl = streamUrl.Trim(),
        };

        _settings.Stations.Add(station);
        Save();
        RaiseChanged();
        return station;
    }

    public bool RemoveStation(string stationId)
    {
        var station = GetStation(stationId);
        if (station is null)
        {
            return false;
        }

        var removed = _settings.Stations.Remove(station);
        if (!removed)
        {
            return false;
        }

        if (string.Equals(_settings.ActiveStationId, stationId, StringComparison.Ordinal))
        {
            _settings.ActiveStationId = null;
        }

        Save();
        RaiseChanged();
        return true;
    }

    public string? ActiveStationId
    {
        get => _settings.ActiveStationId;
        set
        {
            if (string.Equals(_settings.ActiveStationId, value, StringComparison.Ordinal))
            {
                return;
            }

            _settings.ActiveStationId = value;
            Save();
            RaiseChanged();
        }
    }

    public int Volume
    {
        get => _settings.Volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (_settings.Volume == clamped)
            {
                return;
            }

            _settings.Volume = clamped;
            Save();
            RaiseChanged();
        }
    }

    public CommunicationBridgeSettings Communication => _settings.Communication;

    public StationDiscoveryPluginSettings StationDiscoveryPlugins => _settings.StationDiscoveryPlugins;

    public void SaveCommunicationSettings(CommunicationBridgeSettings settings)
    {
        _settings.Communication = new CommunicationBridgeSettings
        {
            EnableNamedPipeRelay = settings.EnableNamedPipeRelay,
            EnableLoopbackRelay = settings.EnableLoopbackRelay,
            LoopbackHost = string.IsNullOrWhiteSpace(settings.LoopbackHost) ? "127.0.0.1" : settings.LoopbackHost.Trim(),
            LoopbackPort = settings.LoopbackPort <= 0 ? 38473 : settings.LoopbackPort,
            EnableProtocolUrlRelay = settings.EnableProtocolUrlRelay,
            ProtocolScheme = string.IsNullOrWhiteSpace(settings.ProtocolScheme) ? "traydio" : settings.ProtocolScheme.Trim().ToLowerInvariant(),
        };

        Save();
        RaiseChanged();
    }

    public void SaveStationDiscoveryPluginSettings(StationDiscoveryPluginSettings settings)
    {
        _settings.StationDiscoveryPlugins = new StationDiscoveryPluginSettings
        {
            PluginDirectory = string.IsNullOrWhiteSpace(settings.PluginDirectory) ? "Plugins" : settings.PluginDirectory.Trim(),
            DisabledPluginIds = settings.DisabledPluginIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };

        Save();
        RaiseChanged();
    }

    private static RadioSettings LoadSettings(string path)
    {
        if (!File.Exists(path))
        {
            return new RadioSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize(json, RadioSettingsJsonContext.Default.RadioSettings);
            if (settings is null)
            {
                return new RadioSettings();
            }

            if (string.IsNullOrWhiteSpace(settings.Communication.LoopbackHost))
            {
                settings.Communication.LoopbackHost = "127.0.0.1";
            }

            if (settings.Communication.LoopbackPort <= 0)
            {
                settings.Communication.LoopbackPort = 38473;
            }

            if (string.IsNullOrWhiteSpace(settings.Communication.ProtocolScheme))
            {
                settings.Communication.ProtocolScheme = "traydio";
            }

            if (string.IsNullOrWhiteSpace(settings.StationDiscoveryPlugins.PluginDirectory))
            {
                settings.StationDiscoveryPlugins.PluginDirectory = "Plugins";
            }

            return settings;
        }
        catch
        {
            return new RadioSettings();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_settings, RadioSettingsJsonContext.Default.RadioSettings);
        File.WriteAllText(_settingsPath, json);
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

