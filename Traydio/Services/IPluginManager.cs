using System;
using System.Collections.Generic;
using Traydio.Common;

namespace Traydio.Services;

public interface IPluginManager
{
    event EventHandler? PluginsChanged;

    IReadOnlyList<ITraydioPlugin> GetPlugins();

    IReadOnlyList<PluginInventoryItem> GetPluginInventory();

    bool AddPlugin(string sourceDllPath, out string? error);

    bool RemovePlugin(string pluginId, out string? error);

    bool SetPluginEnabled(string pluginId, bool enabled, out string? error);

    void Start();

    void Stop();
}

public sealed record PluginInventoryItem(
    string Id,
    string DisplayName,
    string AssemblyName,
    Version Version,
    bool HasSettings,
    bool IsEnabled,
    bool CanUninstall,
    bool IsPendingDelete);
