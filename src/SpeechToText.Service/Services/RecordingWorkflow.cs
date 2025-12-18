using Olbrasoft.SpeechToText.Core.Extensions;
using Olbrasoft.SpeechToText.Core.Interfaces;

namespace Olbrasoft.SpeechToText.Service.Services;

/// <summary>
/// Orchestrates the complete recording workflow: audio capture, transcription, and text output.
/// Extracted from DictationWorker to improve SRP compliance.
/// </summary>
public class RecordingWorkflow : IRecordingWorkflow
{
    private readonly ILogger<RecordingWorkflow> _logger;
    private readonly IAudioRecorder _audioRecorder;
    private readonly ITranscriptionProcessor _transcriptionProcessor;
    private readonly ITextOutputService _textOutputService;
    private readonly IPttNotifier _pttNotifier;
    private readonly IRecordingModeManager _recordingModeManager;

    private bool _isRecording;
    private DateTime? _recordingStartTime;
    private RecordingModeContext? _recordingModeContext;
    private CancellationTokenSource? _transcriptionCts;

    /// <inheritdoc />
    public bool IsRecording => _isRecording;

    /// <inheritdoc />
    public DateTime? RecordingStartTime => _recordingStartTime;

    public RecordingWorkflow(
        ILogger<RecordingWorkflow> logger,
        IAudioRecorder audioRecorder,
        ITranscriptionProcessor transcriptionProcessor,
        ITextOutputService textOutputService,
        IPttNotifier pttNotifier,
        IRecordingModeManager recordingModeManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _audioRecorder = audioRecorder ?? throw new ArgumentNullException(nameof(audioRecorder));
        _transcriptionProcessor = transcriptionProcessor ?? throw new ArgumentNullException(nameof(transcriptionProcessor));
        _textOutputService = textOutputService ?? throw new ArgumentNullException(nameof(textOutputService));
        _pttNotifier = pttNotifier ?? throw new ArgumentNullException(nameof(pttNotifier));
        _recordingModeManager = recordingModeManager ?? throw new ArgumentNullException(nameof(recordingModeManager));
    }

    /// <inheritdoc />
    public async Task StartRecordingAsync()
    {
        if (_isRecording)
        {
            _logger.LogWarning("Recording is already in progress");
            return;
        }

        try
        {
            _isRecording = true;
            _recordingStartTime = DateTime.UtcNow;

            _logger.LogInformation("Starting audio recording...");

            // Enter recording mode (stops TTS, creates lock, saves mute state)
            _recordingModeContext = await _recordingModeManager.EnterRecordingModeAsync();

            // Notify clients about recording start
            await _pttNotifier.NotifyRecordingStartedAsync();

            // Start recording (runs until cancelled)
            var cts = new CancellationTokenSource();
            await _audioRecorder.StartRecordingAsync(cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recording");
            _isRecording = false;
            _recordingStartTime = null;
        }
    }

    /// <inheritdoc />
    public async Task<RecordingWorkflowResult> StopAndProcessAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRecording)
        {
            _logger.LogWarning("No recording in progress");
            return RecordingWorkflowResult.Failed("No recording in progress", 0);
        }

        double durationSeconds = 0;

        try
        {
            _logger.LogInformation("Stopping audio recording...");

            await _audioRecorder.StopRecordingAsync();

            var recordedData = _audioRecorder.GetRecordedData();
            _logger.LogInformation("Recording stopped. Captured {ByteCount} bytes", recordedData.Length);

            // Calculate duration
            if (_recordingStartTime.HasValue)
            {
                durationSeconds = (DateTime.UtcNow - _recordingStartTime.Value).TotalSeconds;
                _logger.LogInformation("Total recording duration: {Duration:F2}s", durationSeconds);
            }

            // Notify clients about recording stop
            await _pttNotifier.NotifyRecordingStoppedAsync(durationSeconds);

            if (recordedData.Length == 0)
            {
                _logger.LogWarning("No audio data recorded");

                // Play rejection sound for empty recording
                Task.Run(async () => await _textOutputService.PlayRejectionSoundAsync()).FireAndForget(_logger, "PlayRejectionSound");

                await _pttNotifier.NotifyTranscriptionFailedAsync("No audio data recorded");
                return RecordingWorkflowResult.Failed("No audio data recorded", durationSeconds);
            }

            // Show icon and play sound IMMEDIATELY (before Whisper processing)
            await _pttNotifier.NotifyTranscriptionStartedAsync();

            // Create cancellation token for transcription
            _transcriptionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                _logger.LogInformation("Starting transcription... (press Escape to cancel)");
                var result = await _transcriptionProcessor.ProcessAsync(recordedData, _transcriptionCts.Token);

                if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
                {
                    // Notify clients about successful transcription
                    await _pttNotifier.NotifyTranscriptionCompletedAsync(result.Text, result.Confidence);

                    // Output text (types and saves to history)
                    await _textOutputService.OutputTextAsync(result.Text);

                    return RecordingWorkflowResult.Succeeded(result.Text, durationSeconds);
                }
                else if (result.WasHallucination)
                {
                    // Play rejection sound for hallucination
                    Task.Run(async () => await _textOutputService.PlayRejectionSoundAsync()).FireAndForget(_logger, "PlayRejectionSound");
                    await _pttNotifier.NotifyTranscriptionFailedAsync(result.ErrorMessage ?? "Whisper hallucination filtered");
                    return RecordingWorkflowResult.Failed(result.ErrorMessage ?? "Whisper hallucination filtered", durationSeconds);
                }
                else
                {
                    _logger.LogWarning("Transcription failed: {Error}", result.ErrorMessage);
                    await _pttNotifier.NotifyTranscriptionFailedAsync(result.ErrorMessage ?? "Transcription failed");
                    return RecordingWorkflowResult.Failed(result.ErrorMessage ?? "Transcription failed", durationSeconds);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Transcription cancelled by user");

                // Play rejection sound for cancellation
                Task.Run(async () => await _textOutputService.PlayRejectionSoundAsync()).FireAndForget(_logger, "PlayRejectionSound");

                await _pttNotifier.NotifyTranscriptionFailedAsync("Transcription cancelled");
                return RecordingWorkflowResult.Cancelled(durationSeconds);
            }
            finally
            {
                _transcriptionCts?.Dispose();
                _transcriptionCts = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop recording");
            await _pttNotifier.NotifyTranscriptionFailedAsync(ex.Message);
            return RecordingWorkflowResult.Failed(ex.Message, durationSeconds);
        }
        finally
        {
            _isRecording = false;
            _recordingStartTime = null;

            // Exit recording mode (releases lock, restores mute state)
            if (_recordingModeContext != null)
            {
                await _recordingModeManager.ExitRecordingModeAsync(_recordingModeContext);
                _recordingModeContext = null;
            }
        }
    }

    /// <summary>
    /// Cancels the current transcription if one is in progress.
    /// </summary>
    public void CancelTranscription()
    {
        if (_transcriptionCts != null && !_transcriptionCts.IsCancellationRequested)
        {
            _logger.LogInformation("Canceling transcription...");
            _transcriptionCts.Cancel();
        }
    }
}
