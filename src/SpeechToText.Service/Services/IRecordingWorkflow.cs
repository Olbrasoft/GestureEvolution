namespace Olbrasoft.SpeechToText.Service.Services;

/// <summary>
/// Result of a recording workflow execution.
/// </summary>
public class RecordingWorkflowResult
{
    public bool Success { get; init; }
    public string? TranscribedText { get; init; }
    public string? ErrorMessage { get; init; }
    public double DurationSeconds { get; init; }
    public bool WasCancelled { get; init; }

    public static RecordingWorkflowResult Succeeded(string text, double duration) =>
        new() { Success = true, TranscribedText = text, DurationSeconds = duration };

    public static RecordingWorkflowResult Failed(string error, double duration) =>
        new() { Success = false, ErrorMessage = error, DurationSeconds = duration };

    public static RecordingWorkflowResult Cancelled(double duration) =>
        new() { Success = false, WasCancelled = true, DurationSeconds = duration };
}

/// <summary>
/// Orchestrates the complete recording workflow: audio capture, transcription, and text output.
/// Extracted from DictationWorker to improve SRP compliance.
/// </summary>
public interface IRecordingWorkflow
{
    /// <summary>
    /// Starts audio recording.
    /// </summary>
    Task StartRecordingAsync();

    /// <summary>
    /// Stops recording and processes the audio: transcribes and outputs text.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel transcription.</param>
    /// <returns>Result of the workflow execution.</returns>
    Task<RecordingWorkflowResult> StopAndProcessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether recording is currently in progress.
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Gets the recording start time if recording is in progress.
    /// </summary>
    DateTime? RecordingStartTime { get; }

    /// <summary>
    /// Cancels the current transcription if one is in progress.
    /// </summary>
    void CancelTranscription();
}
