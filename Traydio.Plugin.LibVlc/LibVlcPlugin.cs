using System.Collections.Generic;
using JetBrains.Annotations;
using Traydio.Common;
using Traydio.Services;

namespace Traydio.Plugin.LibVlc;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class LibVlcPlugin : ITraydioPlugin
{
    public LibVlcPlugin()
    {
        Capabilities = [new RadioPlayerEngineCapability()];
    }

    public string Id => "plugin.playback.libvlc";

    public string DisplayName => "LibVLC Playback";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private sealed class RadioPlayerEngineCapability : IRadioPlayerEngineCapability
    {
        public string CapabilityId => "radio-player-engine";

        public string EngineId => "libvlc";

        public string DisplayName => "LibVLC";

        public IRadioPlayer CreatePlayer() => new LibVlcRadioPlayer();
    }
}

