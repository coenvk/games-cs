using Jentl.Cards;

namespace Poker.Tests;

public class HandEvaluatorTests
{
    [Theory]
    [InlineData("AS KS QS JS 10S 2D 3C", HandCategory.StraightFlush, "Royal flush")]
    [InlineData("5H 4H 3H 2H AH KD KC", HandCategory.StraightFlush, "Straight flush, five high")]
    [InlineData("9C 9D 9H 9S 2D 3C 4H", HandCategory.FourOfAKind, "Four nines")]
    [InlineData("KC KD KH 5S 5D 2C 3H", HandCategory.FullHouse, "Full house, kings over fives")]
    [InlineData("KC KD KH 5S 5D 5C 3H", HandCategory.FullHouse, "Full house, kings over fives")]
    [InlineData("2D 9D JD 4D KD AC AH", HandCategory.Flush, "Flush, king high")]
    [InlineData("9C 8D 7H 6S 5D 2C 2H", HandCategory.Straight, "Straight, nine high")]
    [InlineData("AC 2D 3H 4S 5D 9C KH", HandCategory.Straight, "Straight, five high")]
    [InlineData("10C JD QH KS AD 2C 3H", HandCategory.Straight, "Straight, ace high")]
    [InlineData("7C 7D 7H AS 2D 9C 4H", HandCategory.ThreeOfAKind, "Three sevens")]
    [InlineData("JC JD 4H 4S AD 2C 3H", HandCategory.TwoPair, "Two pair, jacks and fours")]
    [InlineData("6C 6D 9H 2S KD 3C 8H", HandCategory.Pair, "Pair of sixes")]
    [InlineData("AC QD 9H 7S 5D 3C 2H", HandCategory.HighCard, "Ace high")]
    public void Evaluate_FindsTheBestCategory(string cards, HandCategory category, string description)
    {
        var value = HandEvaluator.Evaluate(Cards(cards));

        value.Category.Should().Be(category);
        value.Describe().Should().Be(description);
    }

    [Theory]
    [InlineData("AC AD KH QS 9D", "AC AD KH QS 8D", 1)]
    [InlineData("AC AD KH QS 9D", "AH AS KD QC 9C", 0)]
    [InlineData("2C 2D 3H 3S AD", "2H 2S 3D 3C KD", 1)]
    [InlineData("5D 4C 3H 2S AD", "6D 5C 4H 3S 2D", -1)]
    [InlineData("KC KD KH 2S 2D", "QC QD QH AS AD", 1)]
    [InlineData("2D 3D 4D 5D 7D", "AC KD QH JS 9D", 1)]
    [InlineData("JC JD 4H 4S 2D", "JH JS 4D 4C 3C", -1)]
    public void Comparing_UsesCategoryThenRanksThenKickers(string a, string b, int expected) =>
        Math.Sign(HandEvaluator.Evaluate(Cards(a)).CompareTo(HandEvaluator.Evaluate(Cards(b)))).Should().Be(expected);

    [Fact]
    public void ThreePairs_KeepTheBestTwo_AndTheBestKicker()
    {
        var value = HandEvaluator.Evaluate(Cards("QC QD 8H 8S 3D 3C 7H"));

        value.Category.Should().Be(HandCategory.TwoPair);
        value.Ranks.Should().Equal(12, 8, 7, 0, 0);
    }

    [Fact]
    public void TheBoardCanPlay()
    {
        var board = Cards("AS KS QS JS 10S");

        HandEvaluator.Evaluate([.. board, .. Cards("2C 3D")]).Should().Be(HandEvaluator.Evaluate([.. board, .. Cards("9S 4H")]));
    }

    [Fact]
    public void Evaluate_NeedsFiveToSevenCards()
    {
        var act = () => HandEvaluator.Evaluate(Cards("AS KS QS JS"));

        act.Should().Throw<ArgumentException>();
    }

    internal static Card[] Cards(string codes) => [.. codes.Split(' ').Select(Card.Parse)];
}
