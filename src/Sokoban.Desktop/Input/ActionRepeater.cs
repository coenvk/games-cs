namespace Sokoban.Desktop.Input;

/// <summary>
/// Turns held keys into repeated actions, like a keyboard's auto-repeat.
/// Only the most recently pressed action repeats, so switching direction mid-hold feels right.
/// </summary>
internal sealed class ActionRepeater
{
    private readonly GameAction[] _actions;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _interval;
    private GameAction? _held;
    private TimeSpan _untilNext;

    public ActionRepeater(TimeSpan initialDelay, TimeSpan interval, params GameAction[] actions)
    {
        _actions = actions;
        _initialDelay = initialDelay;
        _interval = interval;
    }

    /// <summary>Returns the action that fires this frame, if any.</summary>
    public GameAction? Update(InputState input, ActionMap map, TimeSpan elapsed)
    {
        foreach (var action in _actions)
        {
            if (map.WasPressed(input, action))
            {
                _held = action;
                _untilNext = _initialDelay;
                return action;
            }
        }

        if (_held is not { } held)
        {
            return null;
        }

        if (!map.IsDown(input, held))
        {
            _held = null;
            return null;
        }

        _untilNext -= elapsed;
        if (_untilNext > TimeSpan.Zero)
        {
            return null;
        }

        _untilNext += _interval;
        return held;
    }
}
