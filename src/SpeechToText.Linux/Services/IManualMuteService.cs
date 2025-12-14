namespace Olbrasoft.SpeechToText.Services;

/// <summary>
/// Service for managing manual mute state (via ScrollLock or tray menu).
/// Thread-safe for concurrent access from tray UI and keyboard monitor.
/// </summary>
public interface IManualMuteService
{
    /// <summary>
    /// Gets whether audio capture is currently muted.
    /// </summary>
    bool IsMuted { get; }

    /// <summary>
    /// Toggles the mute state and returns the new state.
    /// </summary>
    /// <returns>True if now muted, false if now unmuted.</returns>
    bool Toggle();

    /// <summary>
    /// Sets the mute state explicitly.
    /// </summary>
    /// <param name="muted">True to mute, false to unmute.</param>
    void SetMuted(bool muted);

    /// <summary>
    /// Event raised when mute state changes.
    /// </summary>
    event EventHandler<bool>? MuteStateChanged;
}
