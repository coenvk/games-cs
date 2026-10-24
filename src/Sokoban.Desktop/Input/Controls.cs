using Jentl.Core.Input;
using Jentl.Core.UI;
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
    SwitchPack,
    Confirm,
    NextLevel,
    PreviousLevel,
    Back,
    Inventory,
    Journal,
    Wait,
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
        [GameAction.SwitchPack] = [Keys.Tab],
        [GameAction.Confirm] = [Keys.Enter, Keys.Space],
        [GameAction.NextLevel] = [Keys.PageDown, Keys.OemCloseBrackets],
        [GameAction.PreviousLevel] = [Keys.PageUp, Keys.OemOpenBrackets],
        [GameAction.Back] = [Keys.Escape],
        [GameAction.Inventory] = [Keys.I, Keys.B],
        [GameAction.Journal] = [Keys.J, Keys.Q],
        [GameAction.Wait] = [Keys.OemPeriod, Keys.T],
    });

    /// <summary>The same keys for the UI widgets (menus, forms).</summary>
    public static ActionMap<UiAction> Ui { get; } = new(new Dictionary<UiAction, Keys[]>
    {
        [UiAction.Up] = [.. Default.KeysFor(GameAction.Up)],
        [UiAction.Down] = [.. Default.KeysFor(GameAction.Down)],
        [UiAction.Left] = [.. Default.KeysFor(GameAction.Left)],
        [UiAction.Right] = [.. Default.KeysFor(GameAction.Right)],
        [UiAction.Confirm] = [.. Default.KeysFor(GameAction.Confirm)],
        [UiAction.Back] = [.. Default.KeysFor(GameAction.Back)],
    });
}
