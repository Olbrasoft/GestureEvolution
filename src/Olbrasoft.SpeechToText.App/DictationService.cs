using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.Speech;
using Olbrasoft.SpeechToText.TextInput;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// Dictation states for the application.
/// </summary>
public enum DictationState
{
    Idle,
    Recording,
    Transcribing
}

/// <summary>
/// Orchestrates the dictation workflow: recording, transcription, and text typing.
/// </summary>
public class DictationService : IDisposable
{
    private readonly ILogger<DictationService> _logger;
    private readonly IKeyboardMonitor _keyboardMonitor;
    private readonly IAudioRecorder _audioRecorder;
    private readonly ISpeechTranscriber _speechTranscriber;
    private readonly ITextTyper _textTyper;
    private readonly TypingSoundPlayer? _typingSoundPlayer;
    private readonly TextFilter? _textFilter;
    private readonly KeyCode _triggerKey;
    private readonly KeyCode _cancelKey;

    private DictationState _state = DictationState.Idle;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _transcriptionCts;

    /// <summary>
    /// Event raised when dictation state changes.
    /// </summary>
    public event EventHandler<DictationState>? StateChanged;

    /// <summary>
    /// Gets the current dictation state.
    /// </summary>
    public DictationState State => _state;

    public DictationService(
        ILogger<DictationService> logger,
        IKeyboardMonitor keyboardMonitor,
        IAudioRecorder audioRecorder,
        ISpeechTranscriber speechTranscriber,
        ITextTyper textTyper,
        TypingSoundPlayer? typingSoundPlayer = null,
        TextFilter? textFilter = null,
        KeyCode triggerKey = KeyCode.CapsLock,
        KeyCode cancelKey = KeyCode.Escape)
    {
        _logger = logger;
        _keyboardMonitor = keyboardMonitor;
        _audioRecorder = audioRecorder;
        _speechTranscriber = speechTranscriber;
        _textTyper = textTyper;
        _typingSoundPlayer = typingSoundPlayer;
        _textFilter = textFilter;
        _triggerKey = triggerKey;
        _cancelKey = cancelKey;
    }

    /// <summary>
    /// Starts monitoring keyboard for CapsLock trigger.
    /// </summary>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken)
    {
        _keyboardMonitor.KeyReleased += OnKeyReleased;

        _logger.LogInformation("Starting keyboard monitor, trigger key: {TriggerKey}", _triggerKey);
        await _keyboardMonitor.StartMonitoringAsync(cancellationToken);
    }

    /// <summary>
    /// Stops keyboard monitoring.
    /// </summary>
    public async Task StopMonitoringAsync()
    {
        _keyboardMonitor.KeyReleased -= OnKeyReleased;
        await _keyboardMonitor.StopMonitoringAsync();
    }

    private void OnKeyReleased(object? sender, KeyEventArgs e)
    {
        // Handle cancel key during transcription
        if (e.Key == _cancelKey && _state == DictationState.Transcribing)
        {
            _logger.LogInformation("{CancelKey} pressed - cancelling transcription", _cancelKey);
            CancelTranscription();
            return;
        }

        if (e.Key != _triggerKey)
            return;

        _logger.LogDebug("{TriggerKey} released, current state: {State}", _triggerKey, _state);

        // Toggle logic: first press starts recording, second press stops and transcribes
        if (_state == DictationState.Idle)
        {
            _logger.LogInformation("{TriggerKey} pressed - starting dictation", _triggerKey);
            _ = Task.Run(() => StartDictationAsync());
        }
        else if (_state == DictationState.Recording)
        {
            _logger.LogInformation("{TriggerKey} pressed - stopping dictation", _triggerKey);
            _ = Task.Run(() => StopDictationAsync());
        }
        // If Transcribing, ignore the trigger key press (but cancel key is handled above)
    }

    /// <summary>
    /// Cancels ongoing transcription.
    /// </summary>
    private void CancelTranscription()
    {
        _transcriptionCts?.Cancel();
    }

    /// <summary>
    /// Starts recording audio for dictation.
    /// </summary>
    public async Task StartDictationAsync()
    {
        if (_state != DictationState.Idle)
        {
            _logger.LogWarning("Cannot start dictation, current state: {State}", _state);
            return;
        }

        try
        {
            SetState(DictationState.Recording);

            _cts = new CancellationTokenSource();
            _logger.LogInformation("Starting audio recording...");
            await _audioRecorder.StartRecordingAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recording");
            SetState(DictationState.Idle);
        }
    }

    /// <summary>
    /// Stops recording, transcribes, and types the result.
    /// </summary>
    public async Task StopDictationAsync()
    {
        if (_state != DictationState.Recording)
        {
            _logger.LogWarning("Cannot stop dictation, current state: {State}", _state);
            return;
        }

        try
        {
            _logger.LogInformation("Stopping audio recording...");
            await _audioRecorder.StopRecordingAsync();

            var audioData = _audioRecorder.GetRecordedData();
            _logger.LogInformation("Recording stopped. Captured {ByteCount} bytes", audioData.Length);

            if (audioData.Length == 0)
            {
                _logger.LogWarning("No audio data recorded");
                SetState(DictationState.Idle);
                return;
            }

            SetState(DictationState.Transcribing);

            // Create cancellation token for transcription
            _transcriptionCts = new CancellationTokenSource();

            // Start transcription sound loop
            _typingSoundPlayer?.StartLoop();

            _logger.LogInformation("Starting transcription...");
            var result = await _speechTranscriber.TranscribeAsync(audioData, _transcriptionCts.Token);

            // Stop transcription sound loop
            _typingSoundPlayer?.StopLoop();

            if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
            {
                _logger.LogInformation("Transcription: {Text}", result.Text);

                // Apply text filters
                var filteredText = _textFilter?.Apply(result.Text) ?? result.Text;

                if (!string.IsNullOrWhiteSpace(filteredText))
                {
                    // Type the transcribed text
                    await _textTyper.TypeTextAsync(filteredText);
                    _logger.LogInformation("Text typed successfully");
                }
                else
                {
                    _logger.LogInformation("Text empty after filtering, nothing to type");
                }
            }
            else
            {
                _logger.LogWarning("Transcription failed: {Error}", result.ErrorMessage);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Transcription cancelled by user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during transcription");
        }
        finally
        {
            // Ensure sound is stopped even on error/cancel
            _typingSoundPlayer?.StopLoop();
            _transcriptionCts?.Dispose();
            _transcriptionCts = null;
            _cts?.Dispose();
            _cts = null;
            SetState(DictationState.Idle);
        }
    }

    private void SetState(DictationState newState)
    {
        if (_state == newState)
            return;

        _state = newState;
        _logger.LogDebug("State changed to: {State}", _state);
        StateChanged?.Invoke(this, _state);
    }

    public void Dispose()
    {
        _keyboardMonitor.KeyReleased -= OnKeyReleased;
        _cts?.Cancel();
        _cts?.Dispose();
        _typingSoundPlayer?.Dispose();
        _speechTranscriber.Dispose();
    }
}
