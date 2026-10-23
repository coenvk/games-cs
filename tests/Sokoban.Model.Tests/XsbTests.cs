namespace Sokoban.Model.Tests;

public class XsbTests
{
    private const string Simple = """
        #####
        #@$.#
        #####
        """;

    [Fact]
    public void ParseLevel_ReadsWallsGoalsPlayerAndCrates()
    {
        var level = Xsb.ParseLevel(Simple);
        var board = level.Board;

        board.Width.Should().Be(5);
        board.Height.Should().Be(3);
        level.PlayerStart.Should().Be(board.IndexOf(1, 1));
        level.CrateStarts.Should().Equal(board.IndexOf(2, 1));
        board.Goals.Should().Equal(board.IndexOf(3, 1));
        board.IsWall(board.IndexOf(0, 0)).Should().BeTrue();
        board.IsInterior(board.IndexOf(2, 1)).Should().BeTrue();
    }

    [Fact]
    public void ParseLevel_PadsRaggedRowsAndMarksOutsideAsNotInterior()
    {
        var level = Xsb.ParseLevel("""
              ###
            ###.#
            #@$ #
            #####
            """);
        var board = level.Board;

        board.Width.Should().Be(5);
        board.IsInterior(board.IndexOf(0, 0)).Should().BeFalse();
        board.IsWall(board.IndexOf(0, 0)).Should().BeFalse();
    }

    [Theory]
    [InlineData("#####\n#+*$#\n#####", 1, 2)] // player on goal, crate on goal
    [InlineData("#####\n#@-$.#\n######", 1, 1)] // '-' as floor
    public void ParseLevel_SupportsCombinedAndAlternativeTiles(string text, int playerX, int goalCount)
    {
        var level = Xsb.ParseLevel(text);

        level.Board.XOf(level.PlayerStart).Should().Be(playerX);
        level.Board.Goals.Should().HaveCount(goalCount);
    }

    [Theory]
    [InlineData("#####\n# $.#\n#####", "no player")]
    [InlineData("######\n#@$.@#\n######", "more than one player")]
    [InlineData("######\n#@$$.#\n######", "2 crates but 1 goals")]
    [InlineData("#####\n#@  #\n#####", "no goals")]
    [InlineData("#####\n#@$.\n#####", "not enclosed")]
    public void ParseLevel_RejectsInvalidLevels(string text, string reason)
    {
        var act = () => Xsb.ParseLevel(text);

        act.Should().Throw<InvalidLevelException>().WithMessage($"*{reason}*");
    }

    [Fact]
    public void Write_RoundTripsLevel()
    {
        const string text = """
              ###
              #.#
              # ####
            ###$ $.#
            #. $@###
            ####$#
               #.#
               ###

            """;

        Xsb.Write(Xsb.ParseLevel(text)).Should().Be(text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ParsePack_ReadsTitlesFromCommentsAndTitleLines()
    {
        var levels = Xsb.ParsePack("""
            ; First

            #####
            #@$.#
            #####

            #####
            #.$@#
            #####
            Title: Second
            Author: Someone

            #####
            #@*.$#
            ######
            """);

        levels.Select(l => l.Title).Should().Equal("First", "Second", null);
    }

    [Fact]
    public void OriginalLevels_AllParse()
    {
        var levels = Xsb.ParsePack(File.ReadAllText(Path.Combine("levels", "original.xsb")));

        levels.Should().HaveCount(100);
        levels[0].Title.Should().Be("Level 1");
    }
}
