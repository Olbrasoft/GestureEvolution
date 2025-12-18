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
        var iconName = MapGestureToIcon(e.GestureType);
        _trayService.UpdateGestureIcon(e.IsLeftHand, iconName);

        _logger.LogInformation("Gesture {Gesture} detected on {Hand} hand, updating tray icon to {Icon}",
            e.GestureType, e.IsLeftHand ? "left" : "right", iconName);
    }

    private string MapGestureToIcon(GestureType gesture)
    {
        return gesture switch
        {
            GestureType.Fist => "gesture-fist",
            GestureType.Victory => "gesture-victory",
            GestureType.OpenPalm => "gesture-openpalm",
            GestureType.PointingUp => "gesture-pointing",
            GestureType.Ok => "gesture-ok",
            GestureType.ThumbsUp => "gesture-thumbsup",
            GestureType.ThumbsDown => "gesture-thumbsdown",
            GestureType.None => "left-mouse", // Default transparent placeholder
            _ => "left-mouse" // Default for unknown gestures
        };
    }
}
