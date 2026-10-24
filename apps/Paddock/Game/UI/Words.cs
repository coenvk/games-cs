using Paddock;
using static Games.UI.Tr;

namespace Games.UI;

/// <summary>The words for Paddock's enums, translated.</summary>
internal static class Words
{
    public static string Of(Drive drive) => drive switch
    {
        Drive.Fwd => T("FWD"),
        Drive.Rwd => T("RWD"),
        _ => T("4WD"),
    };

    public static string Of(Tyres tyres) => tyres switch
    {
        Tyres.Slick => T("Slick"),
        Tyres.Performance => T("Performance"),
        Tyres.Standard => T("Standard"),
        Tyres.AllSurface => T("All-surface"),
        _ => T("Off-road"),
    };

    public static string Of(Clearance clearance) => clearance switch
    {
        Clearance.Low => T("Low"),
        Clearance.Mid => T("Mid"),
        _ => T("High"),
    };

    public static string Of(Condition condition) => condition switch
    {
        Condition.Dry => T("Dry"),
        Condition.Wet => T("Wet"),
        Condition.Dirt => T("Dirt"),
        Condition.Gravel => T("Gravel"),
        Condition.Snow => T("Snow"),
        Condition.Ice => T("Ice"),
        _ => T("Sand"),
    };

    public static string Of(TrackKind kind) => kind switch
    {
        TrackKind.Drag => T("Drag"),
        TrackKind.Circuit => T("Circuit"),
        TrackKind.Twisty => T("Twisty road"),
        TrackKind.City => T("City"),
        TrackKind.HillClimb => T("Hill climb"),
        _ => T("Off-road"),
    };

    public static string Of(Factor factor) => factor switch
    {
        Factor.TopSpeed => T("Top speed"),
        Factor.Acceleration => T("Acceleration"),
        Factor.Handling => T("Handling"),
        Factor.Weight => T("Weight"),
        Factor.Tyres => T("Tyres"),
        Factor.Drive => T("Drive"),
        _ => T("Clearance"),
    };

    public static string Of(Body body) => body switch
    {
        Body.Hatchback => T("Hatchback"),
        Body.Saloon => T("Saloon"),
        Body.Estate => T("Estate"),
        Body.Coupe => T("Coupé"),
        Body.Roadster => T("Roadster"),
        Body.Muscle => T("Muscle car"),
        Body.Suv => T("SUV"),
        Body.Pickup => T("Pickup"),
        Body.Rally => T("Rally car"),
        Body.Supercar => T("Supercar"),
        Body.Hypercar => T("Hypercar"),
        _ => T("Van"),
    };

    /// <summary>A rule's value as shown (tyres, bodies and countries translated, classes as letters).</summary>
    public static string Name(object value) => value switch
    {
        Tyres tyres => Of(tyres),
        Body body => Of(body),
        string country => T(country),
        _ => value.ToString() ?? string.Empty,
    };

    public static string Of(Area area) => area switch
    {
        Area.Engine => T("Engine"),
        Area.Weight => T("Weight"),
        _ => T("Chassis"),
    };
}
