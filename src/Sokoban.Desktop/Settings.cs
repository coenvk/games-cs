using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sokoban.Desktop;

/// <summary>Player preferences, saved as <c>settings.json</c> next to the progress file.</summary>
internal sealed class Settings
{
    public bool Sound { get; set; } = true;

    public bool Fullscreen { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, SettingsJsonContext.Default.Settings);

    /// <exception cref="InvalidDataException">The JSON is malformed.</exception>
    public static Settings FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.Settings)
                ?? throw new InvalidDataException("Settings file is empty.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("Settings file is not valid JSON.", e);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
