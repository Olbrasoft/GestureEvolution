using Olbrasoft.SpeechToText.Core.Models;

namespace Olbrasoft.SpeechToText.App.Services;

/// <summary>
/// Coordinates the transcription workflow: speech transcription with sound feedback.
/// </summary>
public interface ITranscriptionCoordinator : IDisposable
{
    /// <summary>
    /// Transcribes audio data with sound feedback during processing.
    /// </summary>
    /// <param name="audioData">Audio data to transcribe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transcription result.</returns>
    Task<TranscriptionResult> TranscribeWithFeedbackAsync(byte[] audioData, CancellationToken cancellationToken = default);
}
