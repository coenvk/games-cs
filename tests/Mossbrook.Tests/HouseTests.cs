using Jentl.Puzzle;
using Jentl.Rpg;
using Jentl.Rpg.Maps;
using Jentl.Rpg.World;

namespace Mossbrook.Tests;

public class HouseTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rpg", name));

    [Fact]
    public void EveryHouseMap_HasNoBrokenReferences()
    {
        var content = Adventure.LoadContent(Read);

        foreach (var house in Adventure.Houses)
        {
            ContentValidator.Validate(content, TmxReader.ReadMap(Read($"{house}.tmx"), Read), house).Should().BeEmpty();
        }
    }

    [Fact]
    public void EveryHouse_HasADoorInTheVillage_ThatLeadsInAndBackOut()
    {
        var world = Adventure.Start(Read, seed: 1).World;
        var village = world.Area;

        village.Portals.Where(p => p.Area.StartsWith("house_", StringComparison.Ordinal)).Select(p => p.Area)
            .Should().BeEquivalentTo(Adventure.Houses);
        foreach (var house in Adventure.Houses)
        {
            var door = village.Portals.Single(p => p.Area == house);
            village.Map.IsSolid(door.Cell).Should().BeFalse($"{house}'s door can be walked through");
            var inside = world.GetArea(house);
            inside.IsLit.Should().BeTrue();
            var exit = inside.Portals.Single();
            exit.Area.Should().Be(Adventure.Village);
            Pathfinder.Distance(village.Spawns[exit.Spawn], door.Cell).Should().Be(1, "you come out just outside the door");
            Pathfinder.Find(inside.Spawns["start"], exit.Cell, c => !inside.Map.IsSolid(c)).Should().NotBeNull();
        }
    }

    [Fact]
    public void WalkingThroughTheEldersDoor_GoesInside_AndTheDoormatLeadsOut()
    {
        var world = Adventure.Start(Read, seed: 1).World;
        var door = world.Area.Portals.Single(p => p.Area == "house_elder");
        Walk(world, world.Area.Spawns["elder_door"]);

        world.Step(world.Player.Position.DirectionTo(door.Cell)!.Value);

        world.Area.Id.Should().Be("house_elder");
        world.Area.Name.Should().Be("Elder Maren's house");
        world.Area.Actors.Should().Contain(a => a.Kind == "cat");
        world.Step(GridDirection.Down);
        world.Area.Id.Should().Be(Adventure.Village);
        world.Player.Position.Should().Be(world.Area.Spawns["elder_door"]);
    }

    [Fact]
    public void Villagers_NeverStandInADoorway()
    {
        var world = Adventure.Start(Read, seed: 3).World;
        var doors = world.Area.Portals.Select(p => p.Cell).ToHashSet();

        for (var turn = 0; turn < 300; turn++)
        {
            world.Wait();
            world.Area.Actors.Where(a => a != world.Player).Should().NotContain(a => doors.Contains(a.Position));
        }
    }

    [Fact]
    public void GranHettie_GivesBreadOnce_AndKnowsAboutTheRats()
    {
        var world = Adventure.Start(Read, seed: 1).World;
        var gran = world.GetArea("house_farmer").Actors.Single(a => a.Kind == "gran");

        var first = world.Talk(gran);
        first.Current!.Id.Should().Be("first");
        world.State.Inventory.Count("bread").Should().Be(2);
        first.Continue();
        first.Current!.Id.Should().Be("weather");

        world.State.StartQuest("rats");
        world.Talk(gran).Current!.Id.Should().Be("rats");
        world.State.Inventory.Count("bread").Should().Be(2, "only the first visit gives bread");
    }

    private static void Walk(RpgWorld world, Cell target)
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
