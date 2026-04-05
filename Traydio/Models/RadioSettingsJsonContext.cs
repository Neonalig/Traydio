using System.Text.Json.Serialization;

namespace Traydio.Models;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RadioSettings))]
[JsonSerializable(typeof(CommunicationBridgeSettings))]
[JsonSerializable(typeof(StationDiscoveryPluginSettings))]
internal partial class RadioSettingsJsonContext : JsonSerializerContext;

