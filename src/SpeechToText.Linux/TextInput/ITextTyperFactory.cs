namespace Olbrasoft.SpeechToText.TextInput;

/// <summary>
/// Factory for creating ITextTyper instances based on the current display server.
/// </summary>
public interface ITextTyperFactory
{
    /// <summary>
    /// Creates the appropriate ITextTyper based on the current display server.
    /// </summary>
    /// <returns>An ITextTyper implementation suitable for the current environment.</returns>
    ITextTyper Create();

    /// <summary>
    /// Detects whether the system is running Wayland or X11.
    /// </summary>
    /// <returns>True if running on Wayland, false if X11 or unknown.</returns>
    bool IsWayland();

    /// <summary>
    /// Gets the name of the detected display server.
    /// </summary>
    /// <returns>Display server name for logging purposes.</returns>
    string GetDisplayServerName();
}
