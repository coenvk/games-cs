using Jentl.Core.Content;
using Jentl.Core.Scenes;
using Games.Rendering;
using Games.Scenes;
using Games.UI;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Model;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Sokoban.</summary>
internal static class Entry
{
    public static IGameApp Create() => new SokobanApp();
}

/// <param name="Name">Shown on the level select tab.</param>
/// <param name="Prefix">Used for untitled levels, e.g. "Ice" gives "Ice 3".</param>
internal sealed record LevelPack(string Name, string Prefix, IReadOnlyList<Level> Levels);

/// <summary>A level by its place in the packs.</summary>
internal readonly record struct LevelRef(int Pack, int Index);

/// <summary>Sokoban's levels, sprites and progress, as the shell's context carries them.</summary>
internal sealed record SokobanWorld(IReadOnlyList<LevelPack> Packs, Tileset Tileset, ProgressStore Progress) : IDisposable
{
    public void Dispose() => Tileset.Dispose();
}

/// <summary>Sokoban: push every crate onto a goal. Two packs of levels, a level generator, a solver for hints.</summary>
internal sealed class SokobanApp : IGameApp
{
    public string Title => "Sokoban";

    // Kept from when Sokoban was the only game, so earlier progress carries over.
    public string SaveName => "Sokoban";

    public void Load(GameContext context, GraphicsDevice device)
    {
        LevelPack[] packs =
        [
            new("Classic", "Level", LoadPack("original.xsb")),
            new("Ice", "Ice", LoadPack("ice.xsb")),
        ];
        context.Add(new SokobanWorld(packs, new Tileset(device, context.Assets), new ProgressStore(context.Store)));
    }

    public Scene MainMenu(GameContext context) => new MenuScene(context);

    public IReadOnlyList<Scene> Start(GameContext context) =>
        context.Options.StartLevel is { } level
            ? [new MenuScene(context), new PlayScene(context, new LevelRef(0, Math.Clamp(level, 0, context.Packs[0].Levels.Count - 1)))]
            : [new MenuScene(context)];

    private static IReadOnlyList<Level> LoadPack(string file) => Xsb.ParsePack(GameFiles.ReadAllText($"Levels/{file}"));
}

/// <summary>Sokoban's part of the context: its levels, sprites and progress, and where the player is.</summary>
internal static class SokobanContext
{
    extension(GameContext context)
    {
        public IReadOnlyList<LevelPack> Packs => context.Get<SokobanWorld>().Packs;

        public Tileset Tileset => context.Get<SokobanWorld>().Tileset;

        public ProgressStore Progress => context.Get<SokobanWorld>().Progress;

        public IEnumerable<Level> AllLevels => context.Packs.SelectMany(p => p.Levels);

        /// <summary>Where "Continue" picks up: the first unsolved level, pack by pack; the very first level once all are solved.</summary>
        public LevelRef Continue
        {
            get
            {
                for (var pack = 0; pack < context.Packs.Count; pack++)
                {
                    if (context.Progress.Progress.FirstUnsolved(context.Packs[pack].Levels) is { } index)
                    {
                        return new LevelRef(pack, index);
                    }
                }

                return new LevelRef(0, 0);
            }
        }

        public Level LevelAt(LevelRef where) => context.Packs[where.Pack].Levels[where.Index];

        public string TitleOf(LevelRef where) =>
            context.LevelAt(where).Title ?? $"{T(context.Packs[where.Pack].Prefix)} {where.Index + 1}";

        /// <summary>Picks a level's colours; the classic pack keeps the seeds it always had.</summary>
        public static int ThemeSeed(LevelRef where) => where.Pack * 1000 + where.Index + 1;
    }
}
