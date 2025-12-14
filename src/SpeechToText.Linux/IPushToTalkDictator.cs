namespace Olbrasoft.SpeechToText;

/// <summary>
/// Interface for push-to-talk dictation system.
/// Orchestrates audio recording, speech transcription, and text typing.
/// </summary>
public interface IPushToTalkDictator : IDisposable
{
    /// <summary>
    /// Event raised when text has been transcribed.
    /// </summary>
    event EventHandler<TranscriptionEventArgs>? TextTranscribed;

    /// <summary>
    /// Event raised when an error occurs during dictation.
    /// </summary>
    event EventHandler<ErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Gets a value indicating whether recording is currently active.
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Starts recording and transcription.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartDictationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops recording and performs final transcription.
    /// </summary>
    Task StopDictationAsync();
}
