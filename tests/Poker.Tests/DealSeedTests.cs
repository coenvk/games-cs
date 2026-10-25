namespace Poker.Tests;

public class DealSeedTests
{
    [Fact]
    public void ASeed_IsWrittenAs32HexDigits_AndReadBack()
    {
        var seed = DealSeed.New();

        seed.ToString().Should().HaveLength(32).And.MatchRegex("^[0-9a-f]{32}$");
        DealSeed.Parse(seed.ToString()).Should().Be(seed);
        DealSeed.Parse("2a").Should().Be(DealSeed.FromNumber(42));
        DealSeed.New().Should().NotBe(seed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("-1")]
    [InlineData("0x2a")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    public void TextThatIsNotASeed_IsRefused(string text)
    {
        DealSeed.TryParse(text, out _).Should().BeFalse();
        FluentActions.Invoking(() => DealSeed.Parse(text)).Should().Throw<FormatException>();
    }

    [Fact]
    public void EachHand_HasItsOwnSeed_AndTheSameOneEveryTime()
    {
        var seed = DealSeed.Parse("0f1e2d3c4b5a69788796a5b4c3d2e1f0");

        var hands = Enumerable.Range(0, 50).Select(seed.Hand).ToList();

        hands.Should().OnlyHaveUniqueItems().And.NotContain(seed);
        hands.Should().Equal(Enumerable.Range(0, 50).Select(DealSeed.Parse("0f1e2d3c4b5a69788796a5b4c3d2e1f0").Hand));
    }

    [Fact]
    public void ADeck_HoldsEveryCardOnce_AndIsTheSameForTheSameSeed()
    {
        var seed = DealSeed.FromNumber(7);

        seed.Deck().Should().HaveCount(52).And.OnlyHaveUniqueItems().And.BeEquivalentTo(Jentl.Cards.Deck.Standard52());
        seed.Deck().Should().Equal(DealSeed.FromNumber(7).Deck());
        seed.Deck().Should().NotEqual(DealSeed.FromNumber(8).Deck());
    }
}
