using Composer;
using Jentl.Core.Audio;

namespace Composer.Tests;

public class ComposerSessionTests
{
    [Fact]
    public void Toggle_AddsANote_AndTogglingItOrItsTailRemovesIt()
    {
        var session = new ComposerSession(ComposerSession.Blank());

        session.Toggle(2, 60).Should().BeTrue();
        session.Stretch(2, 60, 5);
        session.CurrentPattern.Notes.Should().Equal(new ComposedNote(2, 60, 4));

        session.Toggle(4, 60).Should().BeFalse();
        session.CurrentPattern.Notes.Should().BeEmpty();
    }

    [Fact]
    public void Undo_AndRedo_StepThroughTheChanges()
    {
        var session = new ComposerSession(ComposerSession.Blank());
        session.Toggle(0, 60);
        session.Toggle(1, 62);
        session.ChangeTempo(10);

        session.Undo();
        session.Song.Tempo.Should().Be(110);
        session.Undo();
        session.CurrentPattern.Notes.Should().Equal(new ComposedNote(0, 60));

        session.Redo();
        session.CurrentPattern.Notes.Should().HaveCount(2);
        session.CanRedo.Should().BeTrue();
        session.Toggle(3, 64);
        session.CanRedo.Should().BeFalse("a new change drops what was undone");
    }

    [Fact]
    public void Place_PutsThePatternInABar_AndTakesItOutAgain()
    {
        var session = new ComposerSession(ComposerSession.Blank());
        session.NewPattern(copy: false);

        session.Place(0, 3);
        session.Song.Channels[0].Arrangement[3].Should().Be(1);
        session.Place(0, 3);
        session.Song.Channels[0].Arrangement[3].Should().Be(-1);
    }

    [Fact]
    public void Pick_PutsABarsPatternOnTheRoll()
    {
        var session = new ComposerSession(ComposerSession.Example());

        session.Pick(1, 2);

        session.Channel.Should().Be(1);
        session.Pattern.Should().Be(2);
    }

    [Fact]
    public void NewPattern_AsACopy_StartsWithTheNotes()
    {
        var session = new ComposerSession(ComposerSession.Blank());
        session.Toggle(0, 60);

        session.NewPattern(copy: true);

        session.Pattern.Should().Be(1);
        session.CurrentPattern.Notes.Should().Equal(new ComposedNote(0, 60));
    }

    [Fact]
    public void Channels_AddUpToALimit_AndTheLastCannotBeRemoved()
    {
        var session = new ComposerSession(new Composition());
        while (session.AddChannel())
        {
        }

        session.Song.Channels.Should().HaveCount(ComposerSession.MaxChannels);
        for (var i = 0; i < 20; i++)
        {
            session.RemoveChannel();
        }

        session.Song.Channels.Should().ContainSingle();
    }

    [Fact]
    public void CycleInstrument_GoesRoundThePresets()
    {
        var session = new ComposerSession(ComposerSession.Blank());
        var first = session.Current.Instrument;

        for (var i = 0; i < ComposerSession.InstrumentNames.Count; i++)
        {
            session.CycleInstrument(1);
        }

        session.Current.Instrument.Should().Be(first);
        session.CycleInstrument(-1);
        Instruments.All.Should().ContainKey(session.Current.Instrument);
    }

    [Fact]
    public void ToggleMetre_DropsNotesPastTheShorterBar()
    {
        var session = new ComposerSession(ComposerSession.Blank());
        session.Toggle(2, 60);
        session.Toggle(14, 60);

        session.ToggleMetre();

        session.Song.StepsPerBar.Should().Be(12);
        session.CurrentPattern.Notes.Should().Equal(new ComposedNote(2, 60));
    }

    [Theory]
    [InlineData(ScaleKind.Major, 0, 64, true)]
    [InlineData(ScaleKind.Major, 0, 63, false)]
    [InlineData(ScaleKind.Minor, 9, 72, true)]
    [InlineData(ScaleKind.Pentatonic, 7, 66, false)]
    [InlineData(ScaleKind.Chromatic, 0, 61, true)]
    public void InScale_FollowsTheKey(ScaleKind scale, int key, int pitch, bool expected)
    {
        var session = new ComposerSession(ComposerSession.Blank()) { Scale = scale, Key = key };

        session.InScale(pitch).Should().Be(expected);
    }

    [Fact]
    public void TheExample_Renders()
    {
        var tune = ComposerSession.Example().ToTune();

        Synth.Render(tune, loop: true).Max(s => Math.Abs((int)s)).Should().BeGreaterThan(3000);
    }

    [Fact]
    public void TheShelf_SavesOpensAndForgetsSongs_NewestFirst()
    {
        var shelf = new SongShelf(null);
        var a = ComposerSession.Example();
        var b = ComposerSession.Blank();
        b.Title = "Second song";

        shelf.Save(a);
        shelf.Save(b);
        shelf.Titles.Should().Equal("Second song", "Example");
        shelf.Open("Example")!.ToJson().Should().Be(a.ToJson());

        shelf.Delete("Second song");
        shelf.Titles.Should().Equal("Example");
        shelf.Open("Second song").Should().BeNull();
        SongShelf.FileOf("Hi, there!").Should().Be("song-hi--there.json");
    }
}
