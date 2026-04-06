using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Traydio.Common;
using Traydio.Services;

namespace Traydio.Plugin.LibVlc;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class LibVlcPlugin : ITraydioPlugin
{
    public const string PLUGIN_ID = "plugin.playback.libvlc";

    public static PluginInstallDisclaimer SettingsDisclaimer { get; } = new()
    {
        Version = "2026-04-06",
        Title = "LibVLC playback information",
        Message =
            "This plugin uses LibVLC/VideoLAN components for playback.\n\n" +
            "Please review license and platform packaging requirements when distributing builds.\n" +
            "Audio output module/device options are advanced settings and may vary by operating system.",
        LinkText = "Open VideoLAN",
        LinkUrl = "https://www.videolan.org/",
        AcceptButtonText = "OK",
        RejectButtonText = "Close",
    };

    private readonly ILogger<LibVlcPlugin> _logger;

    public LibVlcPlugin(ILogger<LibVlcPlugin> logger)
    {
        _logger = logger;
        Capabilities = [new RadioPlayerEngineCapability(), new SettingsCapability()];
        _logger.LogDebug("LibVLC plugin initialized.");
    }

    public string Id => PLUGIN_ID;

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
            var settings = settingsProvider?.GetPluginSettings(PLUGIN_ID);

            string? outputModule = null;
            string? outputDeviceId = null;

            if (settings is not null)
            {
                settings.TryGetValue(LibVlcPluginSettings.OUTPUT_MODULE_KEY, out outputModule);
                settings.TryGetValue(LibVlcPluginSettings.OUTPUT_DEVICE_ID_KEY, out outputDeviceId);
            }

            return new LibVlcRadioPlayer(outputModule, outputDeviceId);
        }
    }

    private sealed class SettingsCapability : IPluginSettingsCapability
    {
        public string CapabilityId => "plugin-settings";

        public string DisplayName => "LibVLC";

        public object CreateSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            return new LibVlcPluginSettingsView(settingsAccessor);
        }
    }
}

