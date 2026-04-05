using System;
using System.Collections.Generic;
using Traydio.Common;

namespace Traydio.Services;

public interface IStationDiscoveryPluginManager
{
    event EventHandler? ProvidersChanged;

    IReadOnlyList<IRadioStationProviderPlugin> GetProviders();

    bool AddPlugin(string sourceDllPath, out string? error);

    bool RemovePlugin(string pluginId, out string? error);

    void Start();

    void Stop();
}

