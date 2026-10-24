using System.Text.Json;
using System.Text.Json.Serialization;

namespace Paddock;

/// <summary>Paddock's data files and saves: camelCase, enums as names.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(RosterFile))]
[JsonSerializable(typeof(TracksFile))]
[JsonSerializable(typeof(Profile))]
[JsonSerializable(typeof(EventsFile))]
internal sealed partial class PaddockJson : JsonSerializerContext;
