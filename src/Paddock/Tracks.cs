using Jentl.Tracks;

namespace Paddock;

/// <summary>The kind of track: decides how a round looks and which stats count most.</summary>
public enum TrackKind { Drag, Circuit, Twisty, City, HillClimb, OffRoad }

/// <summary>One piece of a track: a straight (radius 0) or a corner, optionally up or down hill.</summary>
/// <param name="Length">A straight's length in metres (ignored for corners).</param>
/// <param name="Radius">A corner's radius in metres; 0 for a straight.</param>
/// <param name="Degrees">How far a corner turns: positive left, negative right.</param>
/// <param name="Gradient">Rise over run: 0.08 is an 8% climb, negative goes downhill.</param>
public sealed record TrackPiece(double Length = 0, double Radius = 0, double Degrees = 0, double Gradient = 0)
{
    /// <summary>The piece's length in metres.</summary>
    public double Metres => Radius > 0 ? Radius * Math.Abs(Degrees) * Math.PI / 180 : Length;
}

/// <summary>A track: its pieces in order and the conditions it can be raced in.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Name">Shown name (English; translated through the language file).</param>
/// <param name="Kind">Kind of track.</param>
/// <param name="Pieces">The pieces from start to finish.</param>
/// <param name="Conditions">Conditions a round on this track may have.</param>
public sealed record TrackDef(string Id, string Name, TrackKind Kind, IReadOnlyList<TrackPiece> Pieces, IReadOnlyList<Condition> Conditions)
{
    /// <summary>The track's length in metres.</summary>
    public double Length => Pieces.Sum(p => p.Metres);

    /// <summary>The track's shape, one world unit per metre, starting at the origin heading along +X.</summary>
    public TrackPath ToPath() =>
        TrackPath.Build([.. Pieces.Select(p => p.Radius > 0 ? (PathSegment)new Arc(p.Radius, p.Degrees) : new Straight(p.Length))]);

    /// <summary>Reads <c>tracks.json</c>.</summary>
    public static IReadOnlyList<TrackDef> ListFromJson(string json) =>
        (System.Text.Json.JsonSerializer.Deserialize(json, PaddockJson.Default.TracksFile) ?? throw new FormatException("No tracks.")).Tracks;
}

/// <summary>The contents of <c>tracks.json</c>.</summary>
internal sealed record TracksFile(IReadOnlyList<TrackDef> Tracks);
