using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using JetBrains.Annotations;
using Traydio.Common;
using Traydio.Services;

namespace Traydio.Plugin.ManagedBass;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class ManagedBassPlugin : ITraydioPlugin
{
    public const string PluginId = "plugin.playback.managedbass";

    public ManagedBassPlugin()
    {
        Capabilities = [new RadioPlayerEngineCapability(), new SettingsCapability()];
    }

    public string Id => PluginId;

    public string DisplayName => "ManagedBass Playback";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private sealed class RadioPlayerEngineCapability : IRadioPlayerEngineCapability
    {
        public string CapabilityId => "radio-player-engine";

        public string EngineId => "managedbass";

        public string DisplayName => "ManagedBass";

        public IRadioPlayer CreatePlayer(IServiceProvider serviceProvider)
        {
            var settingsProvider = serviceProvider.GetService<IPluginSettingsProvider>();
            var settings = settingsProvider?.GetPluginSettings(PluginId);

            string? nativeFolder = null;
            string? bassDllPath = null;
            string? bassOpusDllPath = null;
            string? tagsDllPath = null;
            string? outputDeviceIndexText = null;

            if (settings is not null)
            {
                settings.TryGetValue(BassPluginSettings.BassDllPathKey, out bassDllPath);
                settings.TryGetValue(BassPluginSettings.BassOpusDllPathKey, out bassOpusDllPath);
                settings.TryGetValue(BassPluginSettings.TagsDllPathKey, out tagsDllPath);
                settings.TryGetValue(BassPluginSettings.NativeLibraryFolderKey, out nativeFolder);
                settings.TryGetValue(BassPluginSettings.OutputDeviceIndexKey, out outputDeviceIndexText);
            }

            if (!string.IsNullOrWhiteSpace(bassDllPath)
                && string.Equals(Path.GetExtension(bassDllPath), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                nativeFolder = Path.GetDirectoryName(bassDllPath);
            }

            var outputDeviceIndex = int.TryParse(outputDeviceIndexText, out var parsedOutputDeviceIndex)
                ? parsedOutputDeviceIndex
                : (int?)null;

            return new BassRadioPlayer(nativeFolder, outputDeviceIndex, bassOpusDllPath, tagsDllPath);
        }
    }

    private sealed class SettingsCapability : IPluginSettingsCapability
    {
        public string CapabilityId => "plugin-settings";

        public string DisplayName => "ManagedBass";

        public object? CreateSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            if (settingsAccessor is null)
            {
                throw new ArgumentNullException(nameof(settingsAccessor));
            }

            return new BassPluginSettingsView(settingsAccessor);
        }
    }
}
