using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.Audio;
using Olbrasoft.SpeechToText.Core.Models;
using Olbrasoft.SpeechToText.Speech;

namespace Olbrasoft.SpeechToText.App.Services;

/// <summary>
/// Coordinates transcription with audio feedback (typing sound loop).
/// </summary>
public class TranscriptionCoordinator : ITranscriptionCoordinator
{
    private readonly ILogger<TranscriptionCoordinator> _logger;
    private readonly ISpeechTranscriber _speechTranscriber;
    private readonly TypingSoundPlayer? _typingSoundPlayer;
    private bool _disposed;

    public TranscriptionCoordinator(
        ILogger<TranscriptionCoordinator> logger,
        ISpeechTranscriber speechTranscriber,
        TypingSoundPlayer? typingSoundPlayer = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _speechTranscriber = speechTranscriber ?? throw new ArgumentNullException(nameof(speechTranscriber));
        _typingSoundPlayer = typingSoundPlayer;
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeWithFeedbackAsync(
        byte[] audioData,
        CancellationToken cancellationToken = default)
    {
        if (audioData == null || audioData.Length == 0)
        {
            return new TranscriptionResult("No audio data provided");
        }

        try
        {
            // Start transcription sound loop
            _typingSoundPlayer?.StartLoop();

            _logger.LogInformation("Starting transcription of {ByteCount} bytes...", audioData.Length);
            var result = await _speechTranscriber.TranscribeAsync(audioData, cancellationToken);

            return result;
        }
        finally
        {
            // Always stop sound loop
            _typingSoundPlayer?.StopLoop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _typingSoundPlayer?.Dispose();
        _speechTranscriber.Dispose();

        GC.SuppressFinalize(this);
    }
}
