namespace Olbrasoft.SpeechToText;

/// <summary>
/// Linux evdev key codes.
/// Based on /usr/include/linux/input-event-codes.h
/// </summary>
public enum KeyCode
{
    /// <summary>
    /// Unknown or unsupported key.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Escape key (ESC).
    /// </summary>
    Escape = 1,

    /// <summary>
    /// Caps Lock key.
    /// </summary>
    CapsLock = 58,

    /// <summary>
    /// Scroll Lock key.
    /// </summary>
    ScrollLock = 70,

    /// <summary>
    /// Num Lock key.
    /// </summary>
    NumLock = 69,

    /// <summary>
    /// Left Control key.
    /// </summary>
    LeftControl = 29,

    /// <summary>
    /// Right Control key.
    /// </summary>
    RightControl = 97,

    /// <summary>
    /// Left Shift key.
    /// </summary>
    LeftShift = 42,

    /// <summary>
    /// Right Shift key.
    /// </summary>
    RightShift = 54,

    /// <summary>
    /// Left Alt key.
    /// </summary>
    LeftAlt = 56,

    /// <summary>
    /// Right Alt key.
    /// </summary>
    RightAlt = 100,

    /// <summary>
    /// Space bar.
    /// </summary>
    Space = 57,

    /// <summary>
    /// Enter/Return key.
    /// </summary>
    Enter = 28,

    /// <summary>
    /// C key.
    /// </summary>
    C = 46,

    /// <summary>
    /// V key.
    /// </summary>
    V = 47,

    /// <summary>
    /// F8 function key.
    /// </summary>
    F8 = 66
}
