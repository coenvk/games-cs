using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokerBots;

/// <summary>
/// The wire protocol: every message is one line of JSON with a <c>"type"</c> property. Bots in any language can speak
/// it over stdin and stdout; this class is the C# side of it.
/// </summary>
public static class Protocol
{
    /// <summary>The protocol version, sent in <c>hello</c> by both sides.</summary>
    public const int Version = 1;

    /// <summary>Turns a message into one line of JSON (no line breaks, nulls left out).</summary>
    public static string Write(Message message) => JsonSerializer.Serialize(message, ProtocolJson.Default.Message);

    /// <summary>
    /// Reads one line of JSON. Returns null for anything that is not a known message: text that is not JSON, an unknown
    /// <c>type</c>, or a known type with missing or wrong fields. Unknown extra properties are ignored, so newer apps
    /// can add fields without breaking older bots.
    /// </summary>
    public static Message? Read(string line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, ProtocolJson.Default.Message);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentNullException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowOutOfOrderMetadataProperties = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(Hello))]
[JsonSerializable(typeof(MatchStart))]
[JsonSerializable(typeof(HandStart))]
[JsonSerializable(typeof(Decision))]
[JsonSerializable(typeof(HandEnd))]
[JsonSerializable(typeof(MatchEnd))]
[JsonSerializable(typeof(BotAction))]
internal sealed partial class ProtocolJson : JsonSerializerContext;
