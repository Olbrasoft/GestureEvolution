using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.GestureControl.Models;
using Olbrasoft.SpeechToText.GestureControl.Services;
using Olbrasoft.SpeechToText.Service.Tray;

namespace Olbrasoft.SpeechToText.Service.Services;

/// <summary>
/// Bridges gesture detection events to tray icon updates.
/// </summary>
public class GestureTrayBridgeService : IHostedService
{
    private readonly ILogger<GestureTrayBridgeService> _logger;
    private readonly GestureDetectionService _gestureService;
    private readonly TranscriptionTrayService _trayService;

    public GestureTrayBridgeService(
        ILogger<GestureTrayBridgeService> logger,
        GestureDetectionService gestureService,
        TranscriptionTrayService trayService)
    {
        _logger = logger;
        _gestureService = gestureService;
        _trayService = trayService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _gestureService.GestureDetected += OnGestureDetected;
        _logger.LogInformation("GestureTrayBridgeService started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _gestureService.GestureDetected -= OnGestureDetected;
        _logger.LogInformation("GestureTrayBridgeService stopped");
        return Task.CompletedTask;
    }

    private void OnGestureDetected(object? sender, GestureEvent e)
    {
        var iconName = MapGestureToIcon(e.GestureType, e.IsLeftHand, e.IsConfirmed);
        _trayService.UpdateGestureIcon(e.IsLeftHand, iconName);

        _logger.LogInformation("Gesture {Gesture} {Status} on {Hand} hand, updating tray icon to {Icon}",
            e.GestureType, e.IsConfirmed ? "confirmed" : "pending", e.IsLeftHand ? "left" : "right", iconName);
    }

    private string MapGestureToIcon(GestureType gesture, bool isLeftHand, bool isConfirmed)
    {
        // Map gesture type to icon base name
        var baseName = gesture switch
        {
            GestureType.Fist => "fist",
            GestureType.Victory => "victory",
            GestureType.OpenPalm => "stop",
            GestureType.PointingUp => "point",
            GestureType.Ok => "ok",
            GestureType.ThumbsUp => "thumbs-up",
            GestureType.None => null,
            _ => null
        };

        // If no valid gesture, return placeholder
        if (baseName == null)
        {
            return isLeftHand ? "left-mouse" : "right-mouse";
        }

        // Build icon name: {gesture}-[orange-]{left/right}-hand
        var handSide = isLeftHand ? "left" : "right";
        var colorPrefix = isConfirmed ? "orange-" : "";

        return $"{baseName}-{colorPrefix}{handSide}-hand";
    }
}
