namespace Olbrasoft.SpeechToText;

/// <summary>
/// Linux evdev (input subsystem) constants.
/// Centralizes all magic numbers from linux/input.h for maintainability.
/// </summary>
public static class EvdevConstants
{
    /// <summary>
    /// EVIOCGRAB ioctl code for exclusive device grab.
    /// Calculated as _IOW('E', 0x90, int) = 0x40044590.
    /// </summary>
    public const uint EVIOCGRAB = 0x40044590;

    /// <summary>
    /// Size of Linux input_event structure (24 bytes on 64-bit systems).
    /// Structure: timeval (16 bytes) + type (2) + code (2) + value (4).
    /// </summary>
    public const int InputEventSize = 24;

    /// <summary>
    /// Offset to skip timeval in input_event structure.
    /// </summary>
    public const int TimevalOffset = 16;

    /// <summary>
    /// Event type for key/button events.
    /// </summary>
    public const ushort EV_KEY = 0x01;

    /// <summary>
    /// Left mouse button code (BTN_LEFT).
    /// </summary>
    public const ushort BTN_LEFT = 272;   // 0x110

    /// <summary>
    /// Right mouse button code (BTN_RIGHT).
    /// </summary>
    public const ushort BTN_RIGHT = 273;  // 0x111

    /// <summary>
    /// Middle mouse button code (BTN_MIDDLE).
    /// </summary>
    public const ushort BTN_MIDDLE = 274; // 0x112

    /// <summary>
    /// Value indicating key/button press.
    /// </summary>
    public const int KEY_PRESS = 1;

    /// <summary>
    /// Value indicating key/button release.
    /// </summary>
    public const int KEY_RELEASE = 0;

    /// <summary>
    /// Path to Linux input devices information.
    /// </summary>
    public const string DevicesPath = "/proc/bus/input/devices";

    /// <summary>
    /// Default reconnect interval in milliseconds.
    /// </summary>
    public const int DefaultReconnectIntervalMs = 2000;

    /// <summary>
    /// Log interval for "device not found" messages (every N attempts).
    /// With 2000ms interval, 30 attempts = 1 minute.
    /// </summary>
    public const int LogIntervalAttempts = 30;
}
