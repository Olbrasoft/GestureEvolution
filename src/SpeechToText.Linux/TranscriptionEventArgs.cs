namespace Olbrasoft.SpeechToText;

/// <summary>
/// Event arguments for transcription completion.
/// </summary>
public class TranscriptionEventArgs : EventArgs
{
    /// <summary>
    /// Gets the transcribed text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the confidence score (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; }

    /// <summary>
    /// Gets the timestamp when transcription was completed.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TranscriptionEventArgs"/> class.
    /// </summary>
    /// <param name="text">Transcribed text.</param>
    /// <param name="confidence">Confidence score.</param>
    /// <param name="timestamp">Transcription timestamp.</param>
    public TranscriptionEventArgs(string text, float confidence, DateTime timestamp)
    {
        Text = text;
        Confidence = confidence;
        Timestamp = timestamp;
    }
}
