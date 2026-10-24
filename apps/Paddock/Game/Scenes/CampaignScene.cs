using Games.Input;
using Games.UI;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Paddock;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>The campaign: regions of six stages each, raced as often as you like; each stage has three prize cars to win.</summary>
internal sealed class CampaignScene : Scene
{
    private readonly GameContext _context;
    private Menu _menu;

    public CampaignScene(GameContext context)
    {
        _context = context;
        _menu = BuildMenu();
        var open = Stages.FindLastIndex(s => CampaignPlay.IsOpen(Profile, Regions, s));
        _menu.Selected = Math.Max(0, open);
    }

    private Profile Profile => _context.Paddock.Profile;

    private IReadOnlyList<RegionDef> Regions => _context.PaddockData.Regions;

    private List<StageDef> Stages => [.. Regions.SelectMany(r => r.Stages)];

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop(Navigation.Fade());
            return;
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Background);
        var pad = fonts.Px(20);
        batch.DrawText(fonts.Heading, T("Campaign"), new Vector2(viewport.Center.X, viewport.Y + pad), Palette.Accent, Align.Center);
        var line = T("Win a stage to open the next. Each win gives one of its three prize cars.");
        batch.DrawText(fonts.Small, line, new Vector2(viewport.Center.X, viewport.Y + pad + fonts.Heading.LineHeight), Palette.Muted, Align.Center, shadow: false);
        var top = viewport.Y + pad + (int)fonts.Heading.LineHeight + (int)fonts.Small.LineHeight + pad;
        _menu.Draw(batch, fonts.Body, viewport.Center.X, top, viewport.Bottom - top - pad);
    }

    protected override void OnUncovered()
    {
        var selected = _menu.Selected;
        _menu = BuildMenu();
        _menu.Selected = Math.Min(selected, _menu.Items.Count - 1);
    }

    private Menu BuildMenu()
    {
        var items = new List<MenuItem>();
        foreach (var region in Regions)
        {
            foreach (var stage in region.Stages)
            {
                var open = CampaignPlay.IsOpen(Profile, Regions, stage);
                var won = Profile.Campaign.Won(stage);
                var label = open
                    ? T("{0} · RQ {1} · prizes {2}/{3}", StageName(stage), CampaignPlay.HandLimit(Profile, stage), won, stage.PrizeCarIds.Count)
                    : T("{0} · locked", StageName(stage));
                items.Add(new MenuItem(label, () => Race(stage)));
            }
        }

        items.Add(new MenuItem(T("Back"), () => Scenes.Pop(Navigation.Fade())));
        return new Menu(_context.Sounds, [.. items]);
    }

    // "Riverside 3", with the region's name translated.
    private string StageName(StageDef stage)
    {
        var region = Regions.First(r => r.Stages.Contains(stage));
        return T(region.Name) + stage.Name[region.Name.Length..];
    }

    private void Race(StageDef stage)
    {
        if (!CampaignPlay.IsOpen(Profile, Regions, stage))
        {
            _context.Sounds.Play(Sound.Error);
            Scenes.Push(new MessageScene(_context, T("Locked"), T("Win the stage before this one first.")));
            return;
        }

        var data = _context.PaddockData;
        var setup = CampaignPlay.Setup(stage, data.Roster, data.Tracks);
        Scenes.Push(new MatchSetupScene(_context, StageName(stage), setup, CampaignPlay.HandLimit(Profile, stage), _ => true, order =>
        {
            var cars = order.Select(id => Profile.Car(id)!).ToList();
            var outcome = CampaignPlay.Race(Profile, Regions, stage, order, data.Roster, data.Tracks);
            _context.Paddock.Save();
            Scenes.Replace(new RaceScene(_context, setup, cars, outcome.Result, () => Rewards.Show(Scenes, _context, outcome.Result.PlayerWon, outcome.Cash, outcome.Xp, outcome.NewCars)));
        }));
    }
}
