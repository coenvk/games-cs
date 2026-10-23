namespace Uno;

public enum UnoColor
{
    Red,
    Yellow,
    Green,
    Blue,
}

public enum UnoValue
{
    Zero,
    One,
    Two,
    Three,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Skip,
    Reverse,
    DrawTwo,
    Wild,
    WildDrawFour,
}

/// <summary>
/// An Uno card. Wild cards have no <see cref="Color"/> of their own (the colour played is chosen when they
/// are played). <see cref="Copy"/> tells the two copies of most cards apart.
/// </summary>
public readonly record struct UnoCard(UnoValue Value, UnoColor? Color, int Copy = 0)
{
    public bool IsWild => Value is UnoValue.Wild or UnoValue.WildDrawFour;

    public bool IsNumber => Value <= UnoValue.Nine;

    /// <summary>What the card scores for the round's winner when left in a hand: face value, 20 for actions, 50 for wilds.</summary>
    public int Points => IsNumber ? (int)Value : IsWild ? 50 : 20;

    /// <summary>"7", "Skip", "Reverse", "+2", "Wild" or "+4".</summary>
    public string Label => Value switch
    {
        UnoValue.Skip => "Skip",
        UnoValue.Reverse => "Reverse",
        UnoValue.DrawTwo => "+2",
        UnoValue.Wild => "Wild",
        UnoValue.WildDrawFour => "+4",
        _ => ((int)Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public override string ToString() => IsWild ? Value == UnoValue.Wild ? "Wild" : "Wild +4" : $"{Color} {Label}";

    /// <summary>The 108 cards: per colour one 0, two of each 1–9, Skip, Reverse and Draw Two; then four Wilds and four Wild Draw Fours.</summary>
    public static IReadOnlyList<UnoCard> Deck()
    {
        var cards = new List<UnoCard>();
        foreach (var color in Enum.GetValues<UnoColor>())
        {
            cards.Add(new UnoCard(UnoValue.Zero, color));
            for (var value = UnoValue.One; value <= UnoValue.DrawTwo; value++)
            {
                cards.Add(new UnoCard(value, color, 0));
                cards.Add(new UnoCard(value, color, 1));
            }
        }

        for (var copy = 0; copy < 4; copy++)
        {
            cards.Add(new UnoCard(UnoValue.Wild, null, copy));
            cards.Add(new UnoCard(UnoValue.WildDrawFour, null, copy));
        }

        return cards;
    }
}
