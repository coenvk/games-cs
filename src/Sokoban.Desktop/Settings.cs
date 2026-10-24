using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sokoban.Desktop;

/// <summary>Player preferences, saved as <c>settings.json</c> next to the progress file.</summary>
internal sealed class Settings
{
    public bool Sound { get; set; } = true;

    /// <summary>Sound effects volume from 0 to 1.</summary>
    public float Volume { get; set; } = 1f;

    public bool Music { get; set; } = true;

    /// <summary>Music volume from 0 to 1.</summary>
    public float MusicVolume { get; set; } = 0.6f;

    public bool Fullscreen { get; set; }

    /// <summary>A language code ("en", "nl"), or "auto" to follow the system.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Glow, scanlines and a vignette over everything.</summary>
    public bool Retro { get; set; }

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
