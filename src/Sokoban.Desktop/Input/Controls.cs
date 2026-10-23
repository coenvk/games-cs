using Jentl.Core.Input;
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
    Hint,
    Confirm,
    NextLevel,
    PreviousLevel,
    Back,
}

internal static class Controls
{
    public static ActionMap<GameAction> Default { get; } = new(new Dictionary<GameAction, Keys[]>
    {
        [GameAction.Up] = [Keys.Up, Keys.W],
        [GameAction.Down] = [Keys.Down, Keys.S],
        [GameAction.Left] = [Keys.Left, Keys.A],
        [GameAction.Right] = [Keys.Right, Keys.D],
        [GameAction.Undo] = [Keys.Z, Keys.U, Keys.Back],
        [GameAction.Redo] = [Keys.Y],
        [GameAction.Restart] = [Keys.R],
        [GameAction.Hint] = [Keys.H],
        [GameAction.Confirm] = [Keys.Enter, Keys.Space],
        [GameAction.NextLevel] = [Keys.PageDown, Keys.OemCloseBrackets],
        [GameAction.PreviousLevel] = [Keys.PageUp, Keys.OemOpenBrackets],
        [GameAction.Back] = [Keys.Escape],
    });
}
