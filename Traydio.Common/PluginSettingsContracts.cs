using System.Collections.Generic;
using System.Threading.Tasks;

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

    Task<bool> ShowInstallDisclaimerAsync(string pluginId, PluginInstallDisclaimer disclaimer, bool requireAcceptance);
}

public interface IPluginSettingsCapability : IPluginCapability
{
    string DisplayName { get; }

    object? CreateSettingsView(IPluginSettingsAccessor settingsAccessor);
}

public interface IPluginInstallDisclaimerCapability : IPluginCapability
{
    PluginInstallDisclaimer Disclaimer { get; }
}

public sealed class PluginInstallDisclaimer
{
    public required string Version { get; init; }

    public required string Title { get; init; }

    public required string Message { get; init; }

    public string? LinkText { get; init; }

    public string? LinkUrl { get; init; }

    public string AcceptButtonText { get; init; } = "Accept";

    public string RejectButtonText { get; init; } = "Reject";
}

