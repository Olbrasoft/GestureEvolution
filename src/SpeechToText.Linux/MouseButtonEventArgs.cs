namespace Olbrasoft.SpeechToText;

/// <summary>
/// Mouse button enumeration.
/// </summary>
public enum MouseButton
{
    Unknown = 0,
    Left = EvdevConstants.BTN_LEFT,
    Right = EvdevConstants.BTN_RIGHT,
    Middle = EvdevConstants.BTN_MIDDLE
}

/// <summary>
/// Event arguments for mouse button events.
/// </summary>
public class MouseButtonEventArgs : EventArgs
{
    public MouseButtonEventArgs(MouseButton button, ushort rawCode, bool isPressed, DateTime timestamp)
    {
        Button = button;
        RawCode = rawCode;
        IsPressed = isPressed;
        Timestamp = timestamp;
    }

    /// <summary>
    /// Gets the mouse button.
    /// </summary>
    public MouseButton Button { get; }

    /// <summary>
    /// Gets the raw button code from evdev.
    /// </summary>
    public ushort RawCode { get; }

    /// <summary>
    /// Gets whether the button is pressed (true) or released (false).
    /// </summary>
    public bool IsPressed { get; }

    /// <summary>
    /// Gets the timestamp of the event.
    /// </summary>
    public DateTime Timestamp { get; }
}
