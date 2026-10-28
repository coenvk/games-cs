using Jentl.Core.Input;
using Jentl.Core.UI;
using Microsoft.Xna.Framework.Input;
using static Games.UI.Tr;

namespace Games.Input;

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
    public static ActionMap<GameAction> Default { get; } = new(
        new Dictionary<GameAction, Keys[]>
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
        },
        new Dictionary<GameAction, Buttons[]>
        {
            // Buttons may repeat across actions that no scene uses together.
            [GameAction.Up] = [Buttons.DPadUp, Buttons.LeftThumbstickUp],
            [GameAction.Down] = [Buttons.DPadDown, Buttons.LeftThumbstickDown],
            [GameAction.Left] = [Buttons.DPadLeft, Buttons.LeftThumbstickLeft],
            [GameAction.Right] = [Buttons.DPadRight, Buttons.LeftThumbstickRight],
            [GameAction.Undo] = [Buttons.X, Buttons.LeftShoulder],
            [GameAction.Redo] = [Buttons.RightShoulder],
            [GameAction.Restart] = [Buttons.Back],
            [GameAction.Hint] = [Buttons.Y],
            [GameAction.SwitchPack] = [Buttons.LeftShoulder, Buttons.RightShoulder],
            [GameAction.Confirm] = [Buttons.A],
            [GameAction.Back] = [Buttons.B, Buttons.Start],
            [GameAction.Inventory] = [Buttons.Y],
            [GameAction.Journal] = [Buttons.X],
            [GameAction.Wait] = [Buttons.RightShoulder],
        });

    /// <summary>
    /// The same keys and buttons for the UI widgets (menus, forms): the player's, once <see cref="Follow"/> has been
    /// given their map.
    /// </summary>
    public static ActionMap<UiAction> Ui { get; private set; } = UiFor(Default);

    /// <summary>Makes <see cref="Ui"/> use <paramref name="game"/>'s keys, after loading or rebinding them.</summary>
    public static void Follow(ActionMap<GameAction> game) => Ui = UiFor(game);

    /// <summary>The player's controls from <c>controls.json</c>, on top of <see cref="Default"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not bindings.</exception>
    public static ActionMap<GameAction> Parse(string json)
    {
        var map = Default.Copy();
        map.Load(json);
        return map;
    }

    /// <summary>What a control is called on the Controls screen.</summary>
    public static string Name(GameAction action) => action switch
    {
        GameAction.Up => T("Move up"),
        GameAction.Down => T("Move down"),
        GameAction.Left => T("Move left"),
        GameAction.Right => T("Move right"),
        GameAction.Undo => T("Undo"),
        GameAction.Redo => T("Redo"),
        GameAction.Restart => T("Restart"),
        GameAction.Hint => T("Hint"),
        GameAction.SwitchPack => T("Switch pack"),
        GameAction.Confirm => T("Confirm"),
        GameAction.NextLevel => T("Next level"),
        GameAction.PreviousLevel => T("Previous level"),
        GameAction.Back => T("Back"),
        GameAction.Inventory => T("Inventory"),
        GameAction.Journal => T("Journal"),
        GameAction.Wait => T("Wait"),
        _ => action.ToString(),
    };

    private static ActionMap<UiAction> UiFor(ActionMap<GameAction> game) => new(
        new Dictionary<UiAction, Keys[]>
        {
            [UiAction.Up] = [.. game.KeysFor(GameAction.Up)],
            [UiAction.Down] = [.. game.KeysFor(GameAction.Down)],
            [UiAction.Left] = [.. game.KeysFor(GameAction.Left)],
            [UiAction.Right] = [.. game.KeysFor(GameAction.Right)],
            [UiAction.Confirm] = [.. game.KeysFor(GameAction.Confirm)],
            [UiAction.Back] = [.. game.KeysFor(GameAction.Back)],
        },
        new Dictionary<UiAction, Buttons[]>
        {
            [UiAction.Up] = [.. game.ButtonsFor(GameAction.Up)],
            [UiAction.Down] = [.. game.ButtonsFor(GameAction.Down)],
            [UiAction.Left] = [.. game.ButtonsFor(GameAction.Left)],
            [UiAction.Right] = [.. game.ButtonsFor(GameAction.Right)],
            [UiAction.Confirm] = [.. game.ButtonsFor(GameAction.Confirm)],
            [UiAction.Back] = [.. game.ButtonsFor(GameAction.Back)],
        });
}
