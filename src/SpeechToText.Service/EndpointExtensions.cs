using System.Diagnostics;
using Olbrasoft.SpeechToText.Service.Hubs;
using Olbrasoft.SpeechToText.Service.Models;
using Olbrasoft.SpeechToText.Service.Services;

namespace Olbrasoft.SpeechToText.Service;

/// <summary>
/// Extension methods for mapping PushToTalk API endpoints.
/// </summary>
public static class EndpointExtensions
{
    /// <summary>
    /// Maps all PushToTalk API endpoints and SignalR hubs.
    /// </summary>
    public static WebApplication MapPushToTalkEndpoints(this WebApplication app)
    {
        // Map SignalR hub
        app.MapHub<PttHub>("/hubs/ptt");

        // Health check endpoint
        app.MapGet("/", () => Results.Ok(new { service = "Olbrasoft.PushToTalk", status = "running" }));

        // Repeat last transcription endpoint - copies last text to clipboard
        app.MapPost("/api/ptt/repeat", RepeatTranscriptionHandler);

        // Status endpoint - returns current recording state
        app.MapGet("/api/status", GetStatusHandler);

        // Recording control endpoints
        app.MapPost("/api/recording/start", StartRecordingHandler);
        app.MapPost("/api/recording/stop", StopRecordingHandler);
        app.MapPost("/api/recording/toggle", ToggleRecordingHandler);

        return app;
    }

    private static StatusResponse GetStatusHandler(IRecordingStateProvider stateProvider)
    {
        return new StatusResponse
        {
            IsRecording = stateProvider.IsRecording,
            IsTranscribing = stateProvider.IsTranscribing,
            RecordingDurationSeconds = stateProvider.RecordingDuration?.TotalSeconds
        };
    }

    private static async Task<IResult> StartRecordingHandler(
        IRecordingController controller,
        ILogger<Program> logger)
    {
        var started = await controller.StartRecordingRemoteAsync();

        if (started)
        {
            logger.LogInformation("Recording started via API");
            return Results.Ok(new { success = true, message = "Recording started" });
        }

        logger.LogWarning("Recording start via API failed - already recording");
        return Results.BadRequest(new { success = false, error = "Already recording" });
    }

    private static async Task<IResult> StopRecordingHandler(
        IRecordingController controller,
        ILogger<Program> logger)
    {
        var stopped = await controller.StopRecordingRemoteAsync();

        if (stopped)
        {
            logger.LogInformation("Recording stopped via API");
            return Results.Ok(new { success = true, message = "Recording stopped" });
        }

        logger.LogWarning("Recording stop via API failed - not recording");
        return Results.BadRequest(new { success = false, error = "Not recording" });
    }

    private static async Task<IResult> ToggleRecordingHandler(
        IRecordingController controller,
        ILogger<Program> logger)
    {
        var isNowRecording = await controller.ToggleRecordingAsync();

        logger.LogInformation("Recording toggled via API - now: {IsRecording}", isNowRecording ? "recording" : "stopped");
        return Results.Ok(new { success = true, isRecording = isNowRecording });
    }

    private static async Task<IResult> RepeatTranscriptionHandler(
        ITranscriptionHistory history,
        ILogger<Program> logger)
    {
        var lastText = history.LastText;

        if (string.IsNullOrEmpty(lastText))
        {
            logger.LogWarning("Repeat requested but no transcription history available");
            return Results.NotFound(new { success = false, message = "No transcription history available" });
        }

        try
        {
            // Copy to clipboard using wl-copy
            var wlCopyProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "wl-copy",
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            wlCopyProcess.Start();
            await wlCopyProcess.StandardInput.WriteAsync(lastText);
            wlCopyProcess.StandardInput.Close();
            await wlCopyProcess.WaitForExitAsync();

            if (wlCopyProcess.ExitCode != 0)
            {
                var error = await wlCopyProcess.StandardError.ReadToEndAsync();
                logger.LogError("wl-copy failed with exit code {ExitCode}: {Error}", wlCopyProcess.ExitCode, error);
                return Results.StatusCode(500);
            }

            logger.LogInformation("Repeat: copied last transcription to clipboard ({Length} chars)", lastText.Length);
            return Results.Ok(new { success = true, text = lastText, copiedToClipboard = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy text to clipboard");
            return Results.StatusCode(500);
        }
    }
}
