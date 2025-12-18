using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.GestureControl.Models;

namespace Olbrasoft.SpeechToText.GestureControl.Services;

/// <summary>
/// Background service for detecting hand gestures using camera and ONNX models.
/// </summary>
public class GestureDetectionService : BackgroundService
{
    private readonly ILogger<GestureDetectionService> _logger;
    private readonly string _palmModelPath;
    private readonly string _landmarkModelPath;

    private CameraService? _camera;
    private OnnxPalmDetector? _palmDetector;
    private OnnxHandLandmark? _landmarkDetector;

    // Gesture stabilization
    private const int StableFramesRequired = 3; // Need 3 consecutive same gestures
    private GestureType _lastGesture = GestureType.None;
    private int _sameGestureCount = 0;
    private DateTime _lastEventTime = DateTime.MinValue;
    private TimeSpan _eventCooldown = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Raised when a stable gesture is detected.
    /// </summary>
    public event EventHandler<GestureEvent>? GestureDetected;

    public GestureDetectionService(ILogger<GestureDetectionService> logger)
    {
        _logger = logger;

        var baseDir = AppContext.BaseDirectory;
        _palmModelPath = Path.Combine(baseDir, "models", "palm_detection_full_inf_post_192x192.onnx");
        _landmarkModelPath = Path.Combine(baseDir, "models", "hand_landmark_sparse_Nx3x224x224.onnx");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("GestureDetectionService starting...");

            // Initialize camera
            _camera = await CameraService.CreateAsync(0, 640, 480, stoppingToken);
            _logger.LogInformation("Camera initialized: {Width}x{Height}", _camera.FrameWidth, _camera.FrameHeight);

            // Load ONNX models
            _palmDetector = new OnnxPalmDetector(_palmModelPath, scoreThreshold: 0.50f);
            _landmarkDetector = new OnnxHandLandmark(_landmarkModelPath, scoreThreshold: 0.50f);
            _logger.LogInformation("ONNX models loaded");

            // Main detection loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var frame = await _camera.WaitForFrameAsync(TimeSpan.FromMilliseconds(200), stoppingToken);
                    if (frame == null) continue;

                    var palms = _palmDetector.Detect(frame);
                    if (palms.Count == 0)
                    {
                        // No hand detected
                        ResetGestureTracking();
                        continue;
                    }

                    // Process first detected hand
                    var palm = palms[0];
                    var landmarkResult = _landmarkDetector.Detect(frame, palm.NormalizedX, palm.NormalizedY, palm.BoxSize);

                    if (landmarkResult == null)
                    {
                        ResetGestureTracking();
                        continue;
                    }

                    // Classify gesture
                    var gesture = ClassifyGesture(landmarkResult);

                    // Stabilize gesture detection
                    if (gesture == _lastGesture && gesture != GestureType.None)
                    {
                        _sameGestureCount++;

                        if (_sameGestureCount >= StableFramesRequired)
                        {
                            // Gesture is stable (confirmed), check cooldown
                            var now = DateTime.UtcNow;
                            if (now - _lastEventTime > _eventCooldown)
                            {
                                // Raise confirmed event
                                var gestureEvent = new GestureEvent
                                {
                                    GestureType = gesture,
                                    IsLeftHand = landmarkResult.IsLeftHand,
                                    Timestamp = now,
                                    Confidence = landmarkResult.Score,
                                    ExtendedFingers = landmarkResult.ExtendedFingers,
                                    IsConfirmed = true
                                };

                                GestureDetected?.Invoke(this, gestureEvent);
                                _lastEventTime = now;
                                _logger.LogInformation("Gesture confirmed: {Gesture} ({Hand} hand, {Fingers} fingers)",
                                    gesture, landmarkResult.IsLeftHand ? "Left" : "Right", landmarkResult.ExtendedFingers);
                            }
                        }
                    }
                    else
                    {
                        // New gesture detected
                        _lastGesture = gesture;
                        _sameGestureCount = 1;

                        // Emit pending event for immediate feedback
                        if (gesture != GestureType.None)
                        {
                            var pendingEvent = new GestureEvent
                            {
                                GestureType = gesture,
                                IsLeftHand = landmarkResult.IsLeftHand,
                                Timestamp = DateTime.UtcNow,
                                Confidence = landmarkResult.Score,
                                ExtendedFingers = landmarkResult.ExtendedFingers,
                                IsConfirmed = false
                            };

                            GestureDetected?.Invoke(this, pendingEvent);
                            _logger.LogDebug("Gesture pending: {Gesture} ({Hand} hand)",
                                gesture, landmarkResult.IsLeftHand ? "Left" : "Right");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during gesture detection");
                    await Task.Delay(1000, stoppingToken); // Back off on error
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GestureDetectionService failed to start");
        }
        finally
        {
            _logger.LogInformation("GestureDetectionService stopping...");
            await CleanupAsync();
        }
    }

    private GestureType ClassifyGesture(HandLandmarkResult result)
    {
        // Check special gestures first
        if (result.IsOkGesture)
            return GestureType.Ok;

        // Classify based on extended fingers
        var fingers = result.ExtendedFingers;

        return fingers switch
        {
            0 => GestureType.Fist,
            2 => GestureType.Victory, // Peace sign
            5 => GestureType.OpenPalm,
            1 => GestureType.PointingUp,
            _ => GestureType.None
        };
    }

    private void ResetGestureTracking()
    {
        _lastGesture = GestureType.None;
        _sameGestureCount = 0;
    }

    private async Task CleanupAsync()
    {
        if (_camera != null)
            await _camera.DisposeAsync();

        _palmDetector?.Dispose();
        _landmarkDetector?.Dispose();

        _logger.LogInformation("GestureDetectionService cleaned up");
    }
}
