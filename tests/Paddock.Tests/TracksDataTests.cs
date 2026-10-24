namespace Paddock.Tests;

public class TracksDataTests
{
    [Fact]
    public void Tracks_HaveUniqueIds_AndConditions()
    {
        var tracks = GameData.Tracks;

        tracks.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        tracks.Should().OnlyContain(t => t.Conditions.Count > 0 && t.Pieces.Count > 0);
    }

    [Fact]
    public void EveryKindOfTrackExists() =>
        GameData.Tracks.Select(t => t.Kind).Distinct().Should().BeEquivalentTo(Enum.GetValues<TrackKind>());

    [Fact]
    public void CircuitsAndCities_AreClosedLoops() =>
        GameData.Tracks.Where(t => t.Kind is TrackKind.Circuit or TrackKind.City)
            .Should().OnlyContain(t => t.ToPath().IsClosed);

    [Fact]
    public void EveryTrack_CanBeRaced() =>
        GameData.Tracks.Should().OnlyContain(t => t.Conditions.All(c => double.IsFinite(RaceSimulator.Run(TestCars.Hatch, t, c).Time)));
}
