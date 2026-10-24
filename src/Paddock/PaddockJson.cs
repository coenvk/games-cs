using System.Text.Json;
using System.Text.Json.Serialization;

namespace Paddock;

/// <summary>Paddock's data files and saves: camelCase, enums as names.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(RosterFile))]
internal sealed partial class PaddockJson : JsonSerializerContext;
