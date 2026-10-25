using System.Text.Json;
using System.Text.Json.Serialization;
using Jentl.Core.Storage;
using Paddock;

namespace Games;

/// <summary>How the camera follows the player's car in circuit races.</summary>
internal enum RaceCamera
{
    /// <summary>North up: the track stays still and the car turns.</summary>
    Fixed,

    /// <summary>The view turns with the car, so the road runs up the screen.</summary>
    TurnWithCar,

    /// <summary>In 3D, from just behind the car.</summary>
    Chase,
}

/// <summary>Paddock's own settings, next to the shared ones.</summary>
internal sealed class PaddockSettings
{
    public RaceCamera RaceCamera { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, PaddockSettingsJson.Default.PaddockSettings);

    /// <exception cref="InvalidDataException">The text isn't Paddock's settings (the file is then set aside).</exception>
    public static PaddockSettings FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, PaddockSettingsJson.Default.PaddockSettings) ?? new PaddockSettings();
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("Paddock's settings are not valid JSON.", e);
        }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(PaddockSettings))]
internal sealed partial class PaddockSettingsJson : JsonSerializerContext;

/// <summary>Owns the player's profile and Paddock's settings, and saves them; knows the time for tickets and events.</summary>
internal sealed class PaddockStore
{
    private readonly SavedFile<Profile> _profile;
    private readonly TimeProvider _clock;

    /// <param name="store">Where to save, or null to keep everything in memory only.</param>
    /// <param name="roster">The cars, to read the profile against.</param>
    /// <param name="clock">The time, for tickets and the event calendar.</param>
    public PaddockStore(ISaveStore? store, Roster roster, TimeProvider clock)
    {
        _clock = clock;
        _profile = SavedFile<Profile>.Load(store, "profile.json", json => Profile.FromJson(json, roster), p => p.ToJson(), () => Profile.New(roster));
        Settings = SavedFile<PaddockSettings>.Load(store, "paddock.json", PaddockSettings.FromJson, s => s.ToJson(), () => new PaddockSettings());
    }

    public Profile Profile => _profile.Value;

    public SavedFile<PaddockSettings> Settings { get; }

    public DateTimeOffset Now => _clock.GetUtcNow();

    public void Save() => _profile.Save();
}
