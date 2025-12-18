using Olbrasoft.SpeechToText.Core.Extensions;
using Olbrasoft.SpeechToText.Service.Services;

namespace Olbrasoft.SpeechToText.Service;

/// <summary>
/// Background worker service for gesture-controlled dictation.
/// Provides remote control capabilities via IRecordingController interface.
/// Controlled via API/SignalR, not by keyboard events.
/// </summary>
public class DictationWorker : BackgroundService, IRecordingStateProvider, IRecordingController
{
    private readonly ILogger<DictationWorker> _logger;
    private readonly IRecordingWorkflow _recordingWorkflow;
    private readonly IPttNotifier _pttNotifier;

    private bool _isTranscribing;

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
        IRecordingWorkflow recordingWorkflow,
        IPttNotifier pttNotifier)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _recordingWorkflow = recordingWorkflow ?? throw new ArgumentNullException(nameof(recordingWorkflow));
        _pttNotifier = pttNotifier ?? throw new ArgumentNullException(nameof(pttNotifier));

        _logger.LogWarning("=== NOTIFIER HASH: {Hash} ===", _pttNotifier.GetHashCode());
        _logger.LogInformation("Dictation worker initialized (gesture-controlled via API/SignalR)");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Speech-to-Text Dictation Service starting (gesture-controlled)...");

        try
        {
            _logger.LogInformation("Ready for remote control via API/SignalR");

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
            if (_recordingWorkflow.IsRecording)
            {
                await _recordingWorkflow.StopAndProcessAsync();
            }
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
