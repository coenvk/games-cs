using Games.Scenes;
using Jentl.Core.Content;
using Jentl.Core.Raster;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework.Graphics;
using Paddock;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Paddock.</summary>
internal static class Entry
{
    public static IGameApp Create() => new PaddockApp();
}

/// <summary>Paddock: collect invented cars, race hands of five on tracks in all weathers, and win rare ones in events.</summary>
internal sealed class PaddockApp : IGameApp
{
    public string Title => "Paddock";

    public string SaveName => "Paddock";

    public void Load(GameContext context, GraphicsDevice device)
    {
        var data = context.Add(PaddockData.Load(context.Assets));
        context.Add(new PaddockStore(context.Store, data.Roster, TimeProvider.System));
    }

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(T("Garage"), () => title.Open(new GarageScene(context))),
            new(T("Events"), () => title.Open(new EventsScene(context))),
            new(T("Campaign"), () => title.Open(new CampaignScene(context))),
            new(T("Packs"), () => title.Open(new PacksScene(context))),
        ],
        () => T("Collect cars, race them, win rare ones"));

    public IEnumerable<FormRow> Options(GameContext context)
    {
        var store = context.Paddock;
        yield return new ChoiceRow(
            T("Race camera"),
            [T("Fixed"), T("Turn with car"), T("Behind the car (3D)")],
            () => (int)store.Settings.Value.RaceCamera,
            choice =>
            {
                store.Settings.Value.RaceCamera = (RaceCamera)choice;
                store.Settings.Save();
            });
    }
}

/// <summary>Paddock's data, loaded once: the cars, tracks, events, campaign and the car art.</summary>
internal sealed class PaddockData : IDisposable
{
    public const string Folder = "Assets/paddock";

    private const string CarAtlas = "Assets/cars.atlas.json";

    private readonly AssetStore _assets;
    private readonly Dictionary<string, Jentl.Raster.Texture> _pictures = [];
    private Jentl.Assets.Image? _carImage;

    private PaddockData(AssetStore assets, Roster roster, IReadOnlyList<TrackDef> tracks, IReadOnlyList<EventDef> events, IReadOnlyList<RegionDef> regions, TextureAtlas cars)
    {
        _assets = assets;
        Roster = roster;
        Tracks = tracks;
        Events = new EventCalendar(events);
        Regions = regions;
        Cars = cars;
    }

    public Roster Roster { get; }

    public IReadOnlyList<TrackDef> Tracks { get; }

    public EventCalendar Events { get; }

    public IReadOnlyList<RegionDef> Regions { get; }

    /// <summary>Side views (<c>side/&lt;id&gt;</c>) and top views (<c>top/&lt;id&gt;</c>) of every car.</summary>
    public TextureAtlas Cars { get; }

    /// <summary>A sprite of <see cref="Cars"/> (<c>side/&lt;id&gt;</c>) for the software renderer; the atlas image is read the first time.</summary>
    public Jentl.Raster.Texture CarPicture(string name)
    {
        if (!_pictures.TryGetValue(name, out var picture))
        {
            _carImage ??= _assets.Image($"Assets/{Cars.Description.Image}");
            var source = Cars[name].Source;
            picture = _pictures[name] = RasterTextures.From(_carImage, source);
        }

        return picture;
    }

    public static PaddockData Load(AssetStore assets)
    {
        static string Read(string file) => GameFiles.ReadAllText($"{Folder}/{file}");
        return new PaddockData(
            assets,
            Roster.FromJson(Read("cars.json")),
            TrackDef.ListFromJson(Read("tracks.json")),
            EventDef.ListFromJson(Read("events.json")),
            RegionDef.ListFromJson(Read("campaign.json")),
            assets.Atlas(CarAtlas));
    }

    public void Dispose() => Cars.Dispose();
}

/// <summary>Paddock's part of the context.</summary>
internal static class PaddockContext
{
    extension(GameContext context)
    {
        public PaddockStore Paddock => context.Get<PaddockStore>();

        public PaddockData PaddockData => context.Get<PaddockData>();
    }
}
