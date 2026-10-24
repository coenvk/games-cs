using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Rpg.Dialogue;
using Jentl.Rpg.Items;
using Jentl.Rpg.Quests;
using Jentl.Rpg.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;
using Jentl.Core.UI;
using static Sokoban.Desktop.UI.Tr;

namespace Sokoban.Desktop.Scenes;

/// <summary>A conversation: who speaks and what they say in a box at the bottom, with the player's answers below.</summary>
internal sealed class DialogueScene : Scene
{
    private const int DesignPadding = 28;
    private readonly GameContext _context;
    private readonly DialogueRunner _talk;
    private Menu _menu;

    public DialogueScene(GameContext context, DialogueRunner talk)
    {
        _context = context;
        _talk = talk;
        _menu = BuildMenu();
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_talk.IsOver || _context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        if (_talk.Current is not { } node)
        {
            return;
        }

        var fonts = _context.Fonts;
        var padding = fonts.Px(DesignPadding);
        var width = Math.Min(viewport.Width - 2 * fonts.Px(12), fonts.Px(960));
        var lines = fonts.Body.Wrap(node.Text ?? "", width - 2 * padding);
        var lineHeight = (int)fonts.Body.LineHeight;
        var speakerHeight = node.Speaker is null ? 0 : (int)fonts.Body.LineHeight + fonts.Px(6);
        var height = padding + speakerHeight + lines.Count * lineHeight + fonts.Px(16) + _menu.Height(fonts.Body) + padding / 2;
        var panel = new Rectangle(viewport.Center.X - width / 2, viewport.Bottom - height - fonts.Px(12), width, height);

        batch.FillRectangle(viewport, new Color(10, 12, 20, 90));
        batch.FillRectangle(panel, Palette.Panel);
        batch.FillRectangle(new Rectangle(panel.X, panel.Y, panel.Width, 6), Palette.Accent);
        var y = panel.Y + padding;
        if (node.Speaker is { } speaker)
        {
            batch.DrawText(fonts.Body, speaker, new Vector2(panel.X + padding, y), Palette.Accent);
            y += speakerHeight;
        }

        foreach (var line in lines)
        {
            batch.DrawText(fonts.Body, line, new Vector2(panel.X + padding, y), Palette.Text, shadow: false);
            y += lineHeight;
        }

        _menu.Draw(batch, fonts.Body, panel.Center.X, y + fonts.Px(16));
    }

    private Menu BuildMenu()
    {
        var node = _talk.Current;
        if (_talk.Choices.Count == 0)
        {
            return new Menu(_context.Sounds, new MenuItem(node?.Next is null ? T("Goodbye") : T("Continue"), () => Advance(_talk.Continue)));
        }

        return new Menu(_context.Sounds, [.. _talk.Choices.Select((c, i) => new MenuItem(c.Text, () => Advance(() => _talk.Choose(i))))]);
    }

    private void Advance(Action step)
    {
        step();
        if (_talk.IsOver)
        {
            Scenes.Pop();
        }
        else
        {
            _menu = BuildMenu();
        }
    }
}

/// <summary>The bag: items to use or equip, what is worn, and the player's numbers.</summary>
internal sealed class BagScene : Scene
{
    private readonly GameContext _context;
    private readonly AdventureScene _game;
    private Menu _menu;
    private List<string?> _descriptions = [];

    public BagScene(GameContext context, AdventureScene game)
    {
        _context = context;
        _game = game;
        _menu = BuildMenu();
    }

    public override bool IsOverlay => true;

    private RpgWorld World => _game.World;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back) || actions.WasPressed(input, GameAction.Inventory))
        {
            Scenes.Pop();
            return;
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var player = World.Player;
        var lines = new List<PanelLine>
        {
            new(T("Health {0}/{1}   Attack {2}   Defense {3}", player.Hp, player.MaxHp, World.AttackOf(player), World.DefenseOf(player)), Palette.Text),
            new(T("Level {0}, {1}/{2} xp", player.Level, player.Xp, World.XpForNextLevel), Palette.Muted),
        };
        var description = _menu.Selected < _descriptions.Count ? _descriptions[_menu.Selected] : null;
        foreach (var line in fonts.Body.Wrap(description ?? " ", OverlayPanel.TextWidth(viewport, fonts)))
        {
            lines.Add(new PanelLine(line, Palette.Accent));
        }

        OverlayPanel.Draw(batch, viewport, fonts, T("Bag"), lines, _menu);
    }

    private Menu BuildMenu(int selected = 0)
    {
        var items = new List<MenuItem>();
        _descriptions = [];
        foreach (var (slot, item) in World.State.Equipment.Slots.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            items.Add(new MenuItem(T("Take off {0}", item.Name), () => Act(World.Unequip(slot))));
            _descriptions.Add(item.Description);
        }

        foreach (var stack in World.State.Inventory.Stacks)
        {
            var item = stack.Item;
            var label = stack.Count > 1 ? $"{item.Name} x{stack.Count}" : item.Name;
            items.Add(item.IsUsable ? new MenuItem(T("Use {0}", label), () => Act(World.Use(item.Id)))
                : item.IsEquippable ? new MenuItem(T("Equip {0}", label), () => Act(World.Equip(item.Id)))
                : new MenuItem(label, () => { }));
            _descriptions.Add(item.Description);
        }

        if (items.Count == 0)
        {
            _descriptions.Add(T("Your bag is empty."));
        }

        items.Add(new MenuItem(T("Back"), () => Scenes.Pop()));
        _descriptions.Add(null);
        return new Menu(_context.Sounds, [.. items]) { Selected = Math.Min(selected, items.Count - 1) };
    }

    private void Act(TurnResult result)
    {
        _game.Process(result);
        if (World.IsOver)
        {
            Scenes.Pop();
            return;
        }

        _menu = BuildMenu(_menu.Selected);
    }
}

/// <summary>The quests: what to do next for each active one, and the ones already done.</summary>
internal sealed class JournalScene : Scene
{
    private readonly GameContext _context;
    private readonly RpgWorld _world;
    private readonly Menu _menu;

    public JournalScene(GameContext context, RpgWorld world)
    {
        _context = context;
        _world = world;
        _menu = new Menu(context.Sounds, new MenuItem(T("Back"), () => Scenes.Pop()));
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back) || actions.WasPressed(input, GameAction.Journal))
        {
            Scenes.Pop();
            return;
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var width = OverlayPanel.TextWidth(viewport, fonts);
        var lines = new List<PanelLine>();
        void Add(string text, Color color) => lines.AddRange(fonts.Body.Wrap(text, width).Select(l => new PanelLine(l, color)));

        var started = _world.State.Quests.Started.ToArray();
        foreach (var quest in started)
        {
            if (quest.Status == QuestStatus.Completed)
            {
                Add(T("Done: {0}", quest.Definition.Title), Palette.Muted);
                continue;
            }

            Add(quest.Definition.Title, Palette.Accent);
            if (quest.CurrentObjective is { } objective)
            {
                var count = objective.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $" ({quest.Progress(quest.Step)}/{objective.Count})") : "";
                Add(objective.Text + count, Palette.Text);
            }
        }

        if (started.Length == 0)
        {
            Add(T("No quests yet. Talk to the villagers."), Palette.Muted);
        }

        OverlayPanel.Draw(batch, viewport, fonts, T("Journal"), lines, _menu);
    }
}

internal sealed class AdventurePauseScene : Scene
{
    private readonly GameContext _context;
    private readonly Menu _menu;

    public AdventurePauseScene(GameContext context, AdventureScene game)
    {
        _context = context;
        _menu = new Menu(
            context.Sounds,
            new MenuItem(T("Resume"), () => Scenes.Pop()),
            new MenuItem(T("Journal"), () => Scenes.Push(new JournalScene(context, game.World))),
            new MenuItem(T("New adventure"), () =>
            {
                Scenes.Pop();
                game.NewAdventure();
            }),
            new MenuItem(T("Options"), () => Scenes.Push(new OptionsScene(context))),
            new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport) =>
        OverlayPanel.Draw(batch, viewport, _context.Fonts, T("Paused"), [new PanelLine("Mossbrook", Palette.Muted)], _menu);
}

/// <summary>The amulet is home, or the player has fallen.</summary>
internal sealed class AdventureEndScene : Scene
{
    private readonly GameContext _context;
    private readonly AdventureScene _game;
    private readonly bool _won;
    private readonly Menu _menu;

    public AdventureEndScene(GameContext context, AdventureScene game, bool won)
    {
        _context = context;
        _game = game;
        _won = won;
        var items = new List<MenuItem>();
        if (won)
        {
            items.Add(new MenuItem(T("Keep exploring"), () => Scenes.Pop()));
        }

        items.Add(new MenuItem(won ? T("New adventure") : T("Try again"), () =>
        {
            Scenes.Pop();
            game.NewAdventure();
        }));
        items.Add(new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
        _menu = new Menu(context.Sounds, [.. items]);
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input) => _menu.Update(input, Controls.Ui, elapsed);

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var world = _game.World;
        var quests = world.State.Quests;
        var done = quests.All.Count(q => q.Status == QuestStatus.Completed);
        var summary = T("Level {0}, {1} turns, {2} of {3} quests", world.Player.Level, world.Turn, done, quests.All.Count());
        OverlayPanel.Draw(
            batch,
            viewport,
            _context.Fonts,
            _won ? T("Mossbrook is saved!") : T("You have fallen"),
            [new PanelLine(_won ? T("The amulet is home again.") : T("The cellar claims another adventurer."), Palette.Text), new PanelLine(summary, Palette.Muted)],
            _menu);
    }
}

/// <summary>Carry on the saved adventure or start a new one, when there is a save.</summary>
internal sealed class AdventureMenuScene : Scene
{
    private readonly GameContext _context;
    private readonly Menu _menu;

    public AdventureMenuScene(GameContext context)
    {
        _context = context;
        _menu = new Menu(
            context.Sounds,
            new MenuItem(T("Continue"), () => Scenes.Replace(AdventureScene.Continue(context) ?? AdventureScene.New(context), Navigation.Fade())),
            new MenuItem(T("New adventure"), () => Scenes.Replace(AdventureScene.New(context), Navigation.Fade())),
            new MenuItem(T("Back"), () => Scenes.Pop()));
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport) =>
        OverlayPanel.Draw(batch, viewport, _context.Fonts, "Mossbrook", [new PanelLine(T("A new adventure replaces your saved one."), Palette.Muted)], _menu);
}
