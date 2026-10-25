using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using Jentl.Cards;
using Jentl.Collect;

namespace Poker;

/// <summary>
/// The 128-bit seed cards are dealt from: a match's, from which each hand's own is derived, or one hand's. All its
/// numbers come from a <see cref="KeyedRandom"/> keyed with it (HMAC-SHA256), so what a player sees (its own cards, its
/// own seed) gives away neither the seed nor any other card, and there are far too many seeds to try them all. It is
/// written as 32 hexadecimal digits.
/// </summary>
public readonly record struct DealSeed(UInt128 Value)
{
    private const int Bytes = 16;

    /// <summary>A new seed nobody can guess.</summary>
    public static DealSeed New()
    {
        Span<byte> bytes = stackalloc byte[Bytes];
        RandomNumberGenerator.Fill(bytes);
        return new DealSeed(BinaryPrimitives.ReadUInt128BigEndian(bytes));
    }

    /// <summary>
    /// The seed of deal number <paramref name="number"/> (Poker's <c>--deal</c>), for games between people,
    /// where a deal that can be looked up does no harm. It deals as every other seed does.
    /// </summary>
    public static DealSeed FromNumber(int number) => new((uint)number);

    /// <summary>The seed in <paramref name="text"/>: 1 to 32 hexadecimal digits, as <see cref="ToString"/> writes them.</summary>
    /// <exception cref="FormatException">The text is not such a seed.</exception>
    public static DealSeed Parse(string text) =>
        TryParse(text, out var seed) ? seed : throw new FormatException($"\"{text}\" is not a seed of up to 32 hexadecimal digits.");

    /// <summary>Reads a seed of 1 to 32 hexadecimal digits; false (and the zero seed) when <paramref name="text"/> is none.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out DealSeed seed)
    {
        seed = default;
        if (text is null || text.Length is 0 or > 2 * Bytes || !UInt128.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        seed = new DealSeed(value);
        return true;
    }

    /// <summary>The numbers for <paramref name="label"/>: each label has its own, and none tells anything about another's.</summary>
    public KeyedRandom Numbers(string label)
    {
        Span<byte> key = stackalloc byte[Bytes];
        BinaryPrimitives.WriteUInt128BigEndian(key, Value);
        return new KeyedRandom(key, label);
    }

    /// <summary>A seed of its own for <paramref name="label"/>, derived from this one (and telling nothing about it).</summary>
    public DealSeed Derive(string label)
    {
        Span<byte> bytes = stackalloc byte[Bytes];
        Numbers(label).NextBytes(bytes);
        return new DealSeed(BinaryPrimitives.ReadUInt128BigEndian(bytes));
    }

    /// <summary>
    /// The seed of hand <paramref name="index"/> of a match dealt from this seed. It depends only on this seed and the
    /// index, so the same deal comes again in every run that uses the seed.
    /// </summary>
    public DealSeed Hand(int index) => Derive(string.Create(CultureInfo.InvariantCulture, $"deal {index}"));

    /// <summary>The 52 cards shuffled from this seed (Fisher-Yates), first card dealt first.</summary>
    public IReadOnlyList<Card> Deck() => [.. Jentl.Cards.Deck.Shuffled(Jentl.Cards.Deck.Standard52(), Numbers("deck")).Reverse()];

    /// <summary>The seed as 32 hexadecimal digits (lower case).</summary>
    public override string ToString() => Value.ToString("x32", CultureInfo.InvariantCulture);
}
