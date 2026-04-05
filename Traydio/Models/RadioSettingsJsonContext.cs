using System.Text.Json.Serialization;

namespace Traydio.Models;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RadioSettings))]
internal partial class RadioSettingsJsonContext : JsonSerializerContext
{
}

