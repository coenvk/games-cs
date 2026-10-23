using Jentl.Puzzle;
using Jentl.Rpg;
using Jentl.Rpg.Dialogue;
using Jentl.Rpg.Maps;
using Jentl.Rpg.Quests;
using Jentl.Rpg.World;

namespace Mossbrook.Tests;

public class AdventureTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rpg", name));

    [Fact]
    public void Content_HasNoBrokenReferences()
    {
        var content = Adventure.LoadContent(Read);

        ContentValidator.Validate(content).Should().BeEmpty();
        ContentValidator.Validate(content, TmxReader.ReadMap(Read("village.tmx"), Read), Adventure.Village).Should().BeEmpty();
    }

    [Fact]
    public void Tileset_NamesEveryTileTheGameUses()
    {
        var tileset = TmxReader.ReadTileset(Read("tiles.tsx"));
        var content = Adventure.LoadContent(Read);

        foreach (var name in content.Items.All.Select(i => i.Sprite).Concat(content.Actors.All.Select(a => a.Sprite))
            .Concat(["stone_floor", "stone_wall", "torch_wall", "stairs_up", "stairs_down", "rubble"]))
        {
            tileset.TryFind(name, out _).Should().BeTrue($"the game draws '{name}'");
        }
    }

    [Fact]
    public void ANewGame_StartsInTheVillage_WithTheVillagersAndTheWayDown()
    {
        var world = Adventure.Start(Read, seed: 1).World;

        world.Area.Id.Should().Be(Adventure.Village);
        world.Area.IsLit.Should().BeTrue();
        world.Area.Actors.Select(a => a.Tag).Should().Contain(["elder", "herbalist", "smith", "farmer", "child"]);
        world.Area.Portals.Should().ContainSingle(p => p.Area == "cellar1");
        world.Area.CanEnter(world.Area.Spawns["hatch"]).Should().BeTrue();
        world.Area.Map.IsSolid(world.Player.Position).Should().BeFalse();
    }

    [Fact]
    public void TheVillage_IsWalkableFromTheStart_ToEveryVillagerAndTheHatch()
    {
        var area = Adventure.Start(Read, seed: 1).World.Area;
        var start = area.Spawns["start"];

        foreach (var target in area.Actors.Where(a => a.Faction == Faction.Friendly).Select(a => a.Position).Append(area.Portals[0].Cell))
        {
            Pathfinder.Find(start, target, c => !area.Map.IsSolid(c)).Should().NotBeNull($"{target} can be reached");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CellarFloors_LinkUp_AndTheAmuletCanBeReached(int seed)
    {
        var world = Adventure.Start(Read, seed).World;
        var first = world.GetArea("cellar1");
        var second = world.GetArea("cellar2");

        first.IsLit.Should().BeFalse();
        first.Portals.Select(p => (p.Area, p.Spawn)).Should().BeEquivalentTo([(Adventure.Village, "hatch"), ("cellar2", "start")]);
        second.Portals.Select(p => (p.Area, p.Spawn)).Should().BeEquivalentTo([("cellar1", "down")]);
        Reachable(first, first.Spawns["start"], first.Spawns["down"]).Should().BeTrue();
        var amulet = second.Items.Single(i => i.Item.Id == "amulet");
        Reachable(second, second.Spawns["start"], amulet.Cell).Should().BeTrue();
        first.Actors.Should().NotBeEmpty().And.OnlyContain(a => a.Faction == Faction.Hostile);
        first.Items.Count(i => i.Item.Id == "glowcap").Should().Be(2);
    }

    [Fact]
    public void TheCellar_IsTheSameForTheSameSeed()
    {
        string Layout(int seed)
        {
            var area = Adventure.Start(Read, seed).World.GetArea("cellar2");
            return string.Join(";", area.Map.Cells.Select(c => area.Map.Layers[0][c])) + string.Join(";", area.Actors.Select(a => $"{a.Kind}{a.Position}"));
        }

        Layout(7).Should().Be(Layout(7)).And.NotBe(Layout(8));
    }

    [Fact]
    public void ThePlayer_CanGoDownTheHatch_AndBackUp()
    {
        var world = Adventure.Start(Read, seed: 1).World;
        var hatch = world.Area.Portals[0].Cell;
        WalkTo(world, world.Area.Spawns["hatch"]);

        world.Step(world.Player.Position.DirectionTo(hatch)!.Value);
        world.Area.Id.Should().Be("cellar1");
        world.Area.IsVisible(world.Player.Position).Should().BeTrue();

        var entrance = world.Player.Position;
        var away = GridDirections.All.First(d => world.Area.CanEnter(entrance.Step(d)));
        world.Step(away);
        world.Step(away.Opposite());
        world.Area.Id.Should().Be(Adventure.Village);
        world.Player.Position.Should().Be(world.Area.Spawns["hatch"]);
    }

    [Fact]
    public void TheMainQuest_StartsWithTheElder_AndReturningTheAmuletWins()
    {
        var adventure = Adventure.Start(Read, seed: 1);
        var world = adventure.World;
        var elder = world.Area.Actors.Single(a => a.Tag == "elder");

        var talk = world.Talk(elder);
        Choose(talk, "What's wrong?");
        Choose(talk, "I'll get it back.");
        world.State.Quests["amulet"].Status.Should().Be(QuestStatus.Active);
        talk.Continue();
        talk.IsOver.Should().BeTrue();

        world.Talk(elder).Current!.Id.Should().Be("waiting");
        world.State.Give("amulet");
        world.State.Quests["amulet"].CurrentObjective!.Kind.Should().Be(ObjectiveKind.Talk);

        var handIn = world.Talk(elder);
        handIn.Current!.Id.Should().Be("return");
        adventure.IsWon.Should().BeTrue();
        world.State.Inventory.Has("amulet").Should().BeFalse();
        world.State.Inventory.Count("coins").Should().Be(30);
    }

    [Fact]
    public void Wren_TradesGlowcapsForPotionsAndAVest()
    {
        var world = Adventure.Start(Read, seed: 1).World;
        var wren = world.Area.Actors.Single(a => a.Tag == "herbalist");

        var talk = world.Talk(wren);
        Choose(talk, "Glowcaps?");
        Choose(talk, "I'll look for some.");
        world.State.Give("glowcap", 3);
        world.Talk(wren).Current!.Id.Should().Be("return");

        world.State.Quests["glowcaps"].Status.Should().Be(QuestStatus.Completed);
        world.State.Inventory.Count("potion").Should().Be(2);
        world.State.Inventory.Has("vest").Should().BeTrue();
        world.State.Inventory.Has("glowcap").Should().BeFalse();
    }

    [Fact]
    public void Bram_GivesASwordOnce_AndSellsShieldsForCoins()
    {
        var world = Adventure.Start(Read, seed: 1).World;
        var bram = world.Area.Actors.Single(a => a.Tag == "smith");

        world.Talk(bram).Current!.Id.Should().Be("first");
        world.State.Inventory.Count("sword").Should().Be(1);

        var shop = world.Talk(bram);
        shop.Current!.Id.Should().Be("shop");
        shop.Choices.Select(c => c.Text).Should().Equal("Not right now.");

        world.State.Give("coins", 20);
        var buy = world.Talk(bram);
        Choose(buy, "Buy a shield (15 coins)");
        world.State.Inventory.Has("shield").Should().BeTrue();
        world.State.Inventory.Count("coins").Should().Be(5);
        world.State.Inventory.Count("sword").Should().Be(1, "only the first visit gives a sword");
    }

    private static void Choose(DialogueRunner talk, string text)
    {
        var index = talk.Choices.Select(c => c.Text).ToList().IndexOf(text);
        index.Should().BeGreaterThanOrEqualTo(0, $"\"{text}\" is on offer at node '{talk.Current?.Id}'");
        talk.Choose(index);
    }

    private static bool Reachable(Area area, Cell from, Cell to) => Pathfinder.Find(from, to, c => !area.Map.IsSolid(c)) is not null;

    private static void WalkTo(RpgWorld world, Cell target)
    {
        for (var steps = 0; world.Player.Position != target && steps < 200; steps++)
        {
            var path = Pathfinder.Find(world.Player.Position, target, world.Area.CanEnter);
            path.Should().NotBeNull();
            world.Step(world.Player.Position.DirectionTo(path![0])!.Value);
        }

        world.Player.Position.Should().Be(target);
    }
}
