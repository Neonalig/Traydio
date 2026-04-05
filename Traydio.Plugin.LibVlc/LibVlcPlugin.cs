using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using JetBrains.Annotations;
using Traydio.Common;
using Traydio.Services;

namespace Traydio.Plugin.LibVlc;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class LibVlcPlugin : ITraydioPlugin
{
    public const string PluginId = "plugin.playback.libvlc";

    public LibVlcPlugin()
    {
        Capabilities = [new RadioPlayerEngineCapability(), new SettingsCapability()];
    }

    public string Id => PluginId;

    public string DisplayName => "LibVLC Playback";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private sealed class RadioPlayerEngineCapability : IRadioPlayerEngineCapability
    {
        public string CapabilityId => "radio-player-engine";

        public string EngineId => "libvlc";

        public string DisplayName => "LibVLC";

        public IRadioPlayer CreatePlayer(IServiceProvider serviceProvider)
        {
            var settingsProvider = serviceProvider.GetService<IPluginSettingsProvider>();
            var settings = settingsProvider?.GetPluginSettings(PluginId);

            string? outputModule = null;
            string? outputDeviceId = null;

            if (settings is not null)
            {
                settings.TryGetValue(LibVlcPluginSettings.OutputModuleKey, out outputModule);
                settings.TryGetValue(LibVlcPluginSettings.OutputDeviceIdKey, out outputDeviceId);
            }

            return new LibVlcRadioPlayer(outputModule, outputDeviceId);
        }
    }

    private sealed class SettingsCapability : IPluginSettingsCapability
    {
        public string CapabilityId => "plugin-settings";

        public string DisplayName => "LibVLC";

        public object? CreateSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            return new LibVlcPluginSettingsView(settingsAccessor);
        }
    }
}

