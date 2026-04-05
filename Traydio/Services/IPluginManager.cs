using System;
using System.Collections.Generic;
using Traydio.Common;

namespace Traydio.Services;

public interface IPluginManager
{
    event EventHandler? PluginsChanged;

    IReadOnlyList<ITraydioPlugin> GetPlugins();

    bool AddPlugin(string sourceDllPath, out string? error);

    bool RemovePlugin(string pluginId, out string? error);

    void Start();

    void Stop();
}

