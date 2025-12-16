namespace Olbrasoft.SpeechToText.Service.Services;

/// <summary>
/// Controls TTS (Text-to-Speech) services via HTTP API.
/// Communicates with EdgeTTS and VirtualAssistant services to stop speech,
/// manage mute state, and flush message queues.
/// </summary>
public interface ITtsControlService
{
    /// <summary>
    /// Stops all currently playing TTS speech (both EdgeTTS and VirtualAssistant).
    /// </summary>
    Task StopAllSpeechAsync();

    /// <summary>
    /// Flushes the TTS message queue, playing any queued messages.
    /// </summary>
    Task FlushQueueAsync();

    /// <summary>
    /// Gets the current mute state of VirtualAssistant.
    /// </summary>
    /// <returns>True if muted, false if unmuted, null if state could not be retrieved.</returns>
    Task<bool?> GetMuteStateAsync();

    /// <summary>
    /// Sets the mute state of VirtualAssistant.
    /// When muted, the tray icon changes to indicate muted state.
    /// </summary>
    /// <param name="muted">True to mute, false to unmute.</param>
    Task SetMuteAsync(bool muted);

    /// <summary>
    /// Starts speech lock on VirtualAssistant.
    /// Stops current TTS playback and prevents new speech until unlocked.
    /// Uses auto-timeout for safety (default 30s).
    /// </summary>
    /// <param name="timeoutSeconds">Optional timeout in seconds (default 30).</param>
    Task StartSpeechLockAsync(int? timeoutSeconds = null);

    /// <summary>
    /// Stops speech lock on VirtualAssistant.
    /// Unlocks TTS and immediately processes any queued notifications.
    /// </summary>
    Task StopSpeechLockAsync();
}
