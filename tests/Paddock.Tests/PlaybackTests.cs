namespace Paddock.Tests;

public class PlaybackTests
{
    private static readonly Roster Cars = Roster.FromJson(GameData.Read("cars.json"));

    [Theory]
    [InlineData(TrackKind.Drag, 12.0, 6)]
    [InlineData(TrackKind.Drag, 30.0, 8)]
    [InlineData(TrackKind.Circuit, 30.0, 10)]
    [InlineData(TrackKind.Circuit, 60.0, 15)]
    [InlineData(TrackKind.Twisty, 200.0, 20)]
    public void Duration_FitsTheSpec(TrackKind kind, double winnerTime, double seconds) =>
        Playback.Duration(winnerTime, kind).Should().Be(seconds);

    [Fact]
    public void RaceTime_MapsTheWholePlaybackOntoTheWinnersRun()
    {
        Playback.RaceTime(0, 48, TrackKind.Circuit).Should().Be(0);
        Playback.RaceTime(Playback.Duration(48, TrackKind.Circuit), 48, TrackKind.Circuit).Should().Be(48);
    }

    [Fact]
    public void WhenTheWinnerFinishes_TheLoserIsStillBehind()
    {
        var tracks = GameData.Tracks;
        var random = new Random(3);
        for (var i = 0; i < 20; i++)
        {
            var track = tracks[random.Next(tracks.Count)];
            var condition = track.Conditions[random.Next(track.Conditions.Count)];
            var a = Cars.All[random.Next(Cars.All.Count)].Stats;
            var b = Cars.All[random.Next(Cars.All.Count)].Stats;
            var round = Rounds.Decide(a, b, track, condition);
            var (winner, loser) = round.Winner == 0 ? (round.Player, round.Opponent) : (round.Opponent, round.Player);
            if (winner.Time == loser.Time)
            {
                continue;
            }

            var end = Playback.Duration(winner.Time, track.Kind);
            var t = Playback.RaceTime(end, winner.Time, track.Kind);

            winner.DistanceAt(t).Should().Be(winner.Length);
            loser.DistanceAt(t).Should().BeLessThan(loser.Length);
        }
    }
}
