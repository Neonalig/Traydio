using System.Collections.Generic;
using Traydio.Common;

namespace Traydio.Services.Implementations;

public sealed class PluginSettingsProvider(IStationRepository stationRepository) : IPluginSettingsProvider
{
    public IReadOnlyDictionary<string, string> GetPluginSettings(string pluginId)
    {
        return stationRepository.GetPluginSettings(pluginId);
    }

    public void SavePluginSettings(string pluginId, IReadOnlyDictionary<string, string> settings)
    {
        stationRepository.SavePluginSettings(pluginId, settings);
    }
}

