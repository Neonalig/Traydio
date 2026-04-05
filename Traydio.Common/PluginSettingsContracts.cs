using System.Collections.Generic;

namespace Traydio.Common;

public interface IPluginSettingsProvider
{
    IReadOnlyDictionary<string, string> GetPluginSettings(string pluginId);

    void SavePluginSettings(string pluginId, IReadOnlyDictionary<string, string> settings);
}

public interface IPluginSettingsAccessor
{
    string? GetValue(string key);

    void SetValue(string key, string? value);

    void Save();
}

public interface IPluginSettingsCapability : IPluginCapability
{
    string DisplayName { get; }

    object? CreateSettingsView(IPluginSettingsAccessor settingsAccessor);
}

