using System;
using Traydio.Services;

namespace Traydio.Common;

public interface IRadioPlayerEngineCapability : IPluginCapability
{
    string EngineId { get; }

    string DisplayName { get; }

    IRadioPlayer CreatePlayer(IServiceProvider serviceProvider);
}

