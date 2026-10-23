using Microsoft.Xna.Framework.Input;

namespace Sokoban.Desktop.Input;

internal enum GameAction
{
    Up,
    Down,
    Left,
    Right,
    Undo,
    Redo,
    Restart,
    Confirm,
    NextLevel,
    PreviousLevel,
    Quit,
}

/// <summary>Maps game actions to keys, so the rest of the game never looks at raw keys.</summary>
internal sealed class ActionMap
{
    private readonly Dictionary<GameAction, Keys[]> _bindings;

    public ActionMap(Dictionary<GameAction, Keys[]> bindings) => _bindings = bindings;

    public static ActionMap Default { get; } = new(new()
    {
        [GameAction.Up] = [Keys.Up, Keys.W],
        [GameAction.Down] = [Keys.Down, Keys.S],
        [GameAction.Left] = [Keys.Left, Keys.A],
        [GameAction.Right] = [Keys.Right, Keys.D],
        [GameAction.Undo] = [Keys.Z, Keys.U, Keys.Back],
        [GameAction.Redo] = [Keys.Y],
        [GameAction.Restart] = [Keys.R],
        [GameAction.Confirm] = [Keys.Enter, Keys.Space],
        [GameAction.NextLevel] = [Keys.PageDown, Keys.OemCloseBrackets],
        [GameAction.PreviousLevel] = [Keys.PageUp, Keys.OemOpenBrackets],
        [GameAction.Quit] = [Keys.Escape],
    });

    public bool IsDown(InputState input, GameAction action) =>
        _bindings.TryGetValue(action, out var keys) && keys.Any(input.IsDown);

    public bool WasPressed(InputState input, GameAction action) =>
        _bindings.TryGetValue(action, out var keys) && keys.Any(input.WasPressed);
}
