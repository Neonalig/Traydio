using System.Text.Json.Serialization;

namespace Traydio.Models;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RadioSettings))]
[JsonSerializable(typeof(CommunicationBridgeSettings))]
internal partial class RadioSettingsJsonContext : JsonSerializerContext
{
}

