using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.Core.Extensions;
using Olbrasoft.SpeechToText.TextInput;

namespace Olbrasoft.SpeechToText.App.Hubs;

/// <summary>
/// SignalR hub for dictation remote control and notifications.
/// </summary>
public class DictationHub : Hub
{
    private readonly DictationService _dictationService;
    private readonly ITextTyper _textTyper;
    private readonly ILogger<DictationHub> _logger;

    public DictationHub(DictationService dictationService, ITextTyper textTyper, ILogger<DictationHub> logger)
    {
        _dictationService = dictationService;
        _textTyper = textTyper;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Gets the current dictation status.
    /// </summary>
    public Task<StatusResponse> GetStatus()
    {
        return Task.FromResult(new StatusResponse
        {
            IsRecording = _dictationService.State == DictationState.Recording,
            IsTranscribing = _dictationService.State == DictationState.Transcribing
        });
    }

    /// <summary>
    /// Starts dictation recording.
    /// </summary>
    public async Task StartRecording()
    {
        if (_dictationService.State == DictationState.Idle)
        {
            await _dictationService.StartDictationAsync();
        }
    }

    /// <summary>
    /// Stops dictation recording and starts transcription.
    /// </summary>
    public Task StopRecording()
    {
        if (_dictationService.State == DictationState.Recording)
        {
            // Fire-and-forget: don't await so the hub method returns immediately
            // This allows the client to call CancelTranscription while transcription is running
            Task.Run(() => _dictationService.StopDictationAsync()).FireAndForget(_logger, "StopDictation");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Toggles dictation state.
    /// </summary>
    public Task ToggleRecording()
    {
        if (_dictationService.State == DictationState.Idle)
        {
            // StartDictationAsync is already non-blocking
            Task.Run(() => _dictationService.StartDictationAsync()).FireAndForget(_logger, "StartDictation");
        }
        else if (_dictationService.State == DictationState.Recording)
        {
            // Fire-and-forget: don't await so the hub method returns immediately
            // This allows the client to call CancelTranscription while transcription is running
            Task.Run(() => _dictationService.StopDictationAsync()).FireAndForget(_logger, "StopDictation");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cancels ongoing transcription.
    /// </summary>
    public Task CancelTranscription()
    {
        _logger.LogWarning(">>> DictationHub.CancelTranscription called from client {ConnectionId} <<<", Context.ConnectionId);
        _dictationService.CancelTranscription();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends Enter key press.
    /// </summary>
    public Task SendEnter()
    {
        _logger.LogInformation("SendEnter called from client {ConnectionId}", Context.ConnectionId);
        // Fire-and-forget so the hub returns immediately
        // Note: dotool uses "enter" (lowercase), not "Return"
        Task.Run(() => _textTyper.SendKeyAsync("enter")).FireAndForget(_logger, "SendEnter");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Response model for status queries.
/// </summary>
public class StatusResponse
{
    public bool IsRecording { get; set; }
    public bool IsTranscribing { get; set; }
}

/// <summary>
/// Event types for dictation notifications.
/// </summary>
public enum DictationEventType
{
    RecordingStarted = 0,
    RecordingStopped = 1,
    TranscriptionStarted = 2,
    TranscriptionCompleted = 3
}

/// <summary>
/// Event model sent to SignalR clients.
/// </summary>
public class DictationEvent
{
    public DictationEventType EventType { get; set; }
    public string? Text { get; set; }
}
