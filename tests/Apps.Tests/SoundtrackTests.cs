using Games;
using Jentl.Core.Audio;

namespace Apps.Tests;

public class SoundtrackTests
{
    private static readonly string[] Moods =
        [Music.Menu, Music.Puzzle, Music.Cards, Music.Table, Music.Village, Music.Cellar, Music.Race, Music.Kitchen, Music.Words, Music.Arcade];

    public static TheoryData<string> TrackIds => [.. Soundtrack.Tracks.Select(t => t.Id)];

    [Fact]
    public void EveryTrack_HasItsOwnIdAndTitle_AndAKnownMood()
    {
        Soundtrack.Tracks.Should().HaveCountGreaterThanOrEqualTo(30);
        Soundtrack.Tracks.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        Soundtrack.Tracks.Select(t => t.Title).Should().OnlyHaveUniqueItems();
        Soundtrack.Tracks.Select(t => t.Mood).Should().OnlyContain(m => Moods.Contains(m));
    }

    [Fact]
    public void EveryMood_HasSongsToChooseFrom()
    {
        foreach (var mood in Moods)
        {
            Soundtrack.Playlist(mood).Should().HaveCountGreaterThanOrEqualTo(2, mood);
        }
    }

    [Theory]
    [MemberData(nameof(TrackIds))]
    public void EveryTrack_Renders_ToSomethingWorthListeningTo(string id)
    {
        var track = Soundtrack.Find(id)!;
        var tune = track.Tune();
        var samples = Synth.Render(tune, loop: true);

        (samples.Length / (double)Synth.SampleRate).Should().BeGreaterThan(12, "{0} should last more than a few bars", track.Title);
        samples.Max(s => Math.Abs((int)s)).Should().BeInRange(2000, short.MaxValue, "{0} should be audible and not clip", track.Title);
    }
}
