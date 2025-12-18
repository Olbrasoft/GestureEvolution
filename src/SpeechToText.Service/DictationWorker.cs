using Olbrasoft.SpeechToText.Audio;
using Olbrasoft.SpeechToText.Core.Extensions;
using Olbrasoft.SpeechToText.Service.Services;

namespace Olbrasoft.SpeechToText.Service;

/// <summary>
/// Background worker service for push-to-talk dictation.
/// Monitors keyboard for CapsLock state changes and controls audio recording.
/// Records when CapsLock is ON, stops and transcribes when CapsLock is OFF.
/// Also provides remote control capabilities via IRecordingController interface.
/// </summary>
public class DictationWorker : BackgroundService, IRecordingStateProvider, IRecordingController
{
    private readonly ILogger<DictationWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IKeyboardMonitor _keyboardMonitor;
    private readonly IRecordingWorkflow _recordingWorkflow;
    private readonly IPttNotifier _pttNotifier;
    private readonly ManualMuteService _manualMuteService;

    private bool _isTranscribing;
    private KeyCode _triggerKey;

    // IRecordingStateProvider implementation
    /// <inheritdoc />
    public bool IsRecording => _recordingWorkflow.IsRecording;

    /// <inheritdoc />
    public bool IsTranscribing => _isTranscribing;

    /// <inheritdoc />
    public TimeSpan? RecordingDuration => _recordingWorkflow.RecordingStartTime.HasValue
        ? DateTime.UtcNow - _recordingWorkflow.RecordingStartTime.Value
        : null;

    public DictationWorker(
        ILogger<DictationWorker> logger,
        IConfiguration configuration,
        IKeyboardMonitor keyboardMonitor,
        IRecordingWorkflow recordingWorkflow,
        IPttNotifier pttNotifier,
        ManualMuteService manualMuteService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _keyboardMonitor = keyboardMonitor ?? throw new ArgumentNullException(nameof(keyboardMonitor));
        _recordingWorkflow = recordingWorkflow ?? throw new ArgumentNullException(nameof(recordingWorkflow));
        _pttNotifier = pttNotifier ?? throw new ArgumentNullException(nameof(pttNotifier));
        _manualMuteService = manualMuteService ?? throw new ArgumentNullException(nameof(manualMuteService));

        // Load configuration
        var triggerKeyName = _configuration.GetValue<string>("PushToTalkDictation:TriggerKey", "CapsLock");
        _triggerKey = Enum.Parse<KeyCode>(triggerKeyName);

        _logger.LogWarning("=== NOTIFIER HASH: {Hash} ===", _pttNotifier.GetHashCode());
        _logger.LogInformation("Dictation worker initialized. Trigger key: {TriggerKey}", _triggerKey);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Push-to-Talk Dictation Service starting...");

        try
        {
            // Subscribe to keyboard events
            _keyboardMonitor.KeyPressed += OnKeyPressed;
            _keyboardMonitor.KeyReleased += OnKeyReleased;

            _logger.LogInformation("Press {TriggerKey} to start dictation, release to stop", _triggerKey);

            // Start keyboard monitoring (doesn't block)
            await _keyboardMonitor.StartMonitoringAsync(stoppingToken);

            // Wait indefinitely until cancellation is requested
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Dictation service is stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in dictation worker");
            throw;
        }
        finally
        {
            _keyboardMonitor.KeyPressed -= OnKeyPressed;
            _keyboardMonitor.KeyReleased -= OnKeyReleased;

            if (_recordingWorkflow.IsRecording)
            {
                await _recordingWorkflow.StopAndProcessAsync();
            }
        }
    }

    private void OnKeyPressed(object? sender, KeyEventArgs e)
    {
        // We only care about the trigger key
        // Actual action is taken in OnKeyReleased after LED state updates
    }

    /// <summary>
    /// Handles ScrollLock (ManualMute) key press - toggles mute state and broadcasts to all clients.
    /// Uses internal state tracking since ScrollLock LED doesn't work on Wayland.
    /// </summary>
    private async Task HandleScrollLockPressedAsync()
    {
        try
        {
            // Toggle internal mute state (LED doesn't work on Wayland)
            var newMuteState = _manualMuteService.Toggle();

            _logger.LogInformation("ScrollLock pressed - ManualMute: {State}",
                newMuteState ? "MUTED" : "UNMUTED");

            // Broadcast to all clients
            await _pttNotifier.NotifyManualMuteChangedAsync(newMuteState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle ScrollLock press");
        }
    }

    private void OnKeyReleased(object? sender, KeyEventArgs e)
    {
        // Handle ScrollLock (ManualMute) - toggle on key release
        if (e.Key == KeyCode.ScrollLock)
        {
            Task.Run(async () => await HandleScrollLockPressedAsync()).FireAndForget(_logger, "HandleScrollLockPressed");
            return;
        }

        // Handle Escape - cancel ongoing transcription
        if (e.Key == KeyCode.Escape)
        {
            if (_isTranscribing)
            {
                _logger.LogInformation("Escape pressed - canceling transcription");
                _recordingWorkflow.CancelTranscription();
            }
            return;
        }

        if (e.Key != _triggerKey)
            return;

        // Read actual CapsLock state from LED - this is the reliable source of truth
        // LED state is updated by the kernel AFTER the key event is processed
        // Small delay to ensure LED state is updated
        Thread.Sleep(50);

        var capsLockOn = _keyboardMonitor.IsCapsLockOn();
        _logger.LogDebug("CapsLock released - LED state: {CapsLockOn}, Recording state: {Recording}", capsLockOn, _recordingWorkflow.IsRecording);

        if (capsLockOn && !_recordingWorkflow.IsRecording)
        {
            // CapsLock is ON and not recording - start recording
            _logger.LogInformation("CapsLock ON - starting dictation");
            Task.Run(async () => await StartRecordingAsync()).FireAndForget(_logger, "StartRecording");
        }
        else if (!capsLockOn && _recordingWorkflow.IsRecording)
        {
            // CapsLock is OFF and recording - stop and transcribe
            _logger.LogInformation("CapsLock OFF - stopping dictation");
            Task.Run(async () => await StopRecordingAsync()).FireAndForget(_logger, "StopRecording");
        }
        else
        {
            _logger.LogDebug("CapsLock state ({CapsLockOn}) matches recording state ({Recording}) - no action needed",
                capsLockOn, _recordingWorkflow.IsRecording);
        }
    }

    private async Task StartRecordingAsync()
    {
        await _recordingWorkflow.StartRecordingAsync();
    }

    private async Task StopRecordingAsync()
    {
        _isTranscribing = true;
        try
        {
            await _recordingWorkflow.StopAndProcessAsync();
        }
        finally
        {
            _isTranscribing = false;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Dictation service stopping...");

        if (_recordingWorkflow.IsRecording)
        {
            await _recordingWorkflow.StopAndProcessAsync(cancellationToken);
        }

        await _keyboardMonitor.StopMonitoringAsync();

        await base.StopAsync(cancellationToken);
    }

    // IRecordingController implementation

    /// <inheritdoc />
    public async Task<bool> StartRecordingRemoteAsync(CancellationToken cancellationToken = default)
    {
        if (_recordingWorkflow.IsRecording)
        {
            _logger.LogWarning("Remote start requested but recording is already in progress");
            return false;
        }

        _logger.LogInformation("Remote recording start requested");
        await StartRecordingAsync();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> StopRecordingRemoteAsync(CancellationToken cancellationToken = default)
    {
        if (!_recordingWorkflow.IsRecording)
        {
            _logger.LogWarning("Remote stop requested but no recording in progress");
            return false;
        }

        _logger.LogInformation("Remote recording stop requested");
        await StopRecordingAsync();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ToggleRecordingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Remote recording toggle requested, current state: {IsRecording}", _recordingWorkflow.IsRecording);

        if (_recordingWorkflow.IsRecording)
        {
            await StopRecordingAsync();
            return false; // Now stopped
        }
        else
        {
            await StartRecordingAsync();
            return true; // Now recording
        }
    }
}
