using System.Collections.Generic;
using JetBrains.Annotations;
using Traydio.Common;
using Traydio.Services;

namespace Traydio.Plugin.ManagedBass;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class ManagedBassPlugin : ITraydioPlugin
{
    public ManagedBassPlugin()
    {
        Capabilities = [new RadioPlayerEngineCapability()];
    }

    public string Id => "plugin.playback.managedbass";

    public string DisplayName => "ManagedBass Playback";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private sealed class RadioPlayerEngineCapability : IRadioPlayerEngineCapability
    {
        public string CapabilityId => "radio-player-engine";

        public string EngineId => "managedbass";

        public string DisplayName => "ManagedBass";

        public IRadioPlayer CreatePlayer() => new BassRadioPlayer();
    }
}

