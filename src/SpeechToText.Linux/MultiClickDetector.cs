using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText;

/// <summary>
/// Detects multi-click patterns (single, double, triple click) with timing-based detection.
/// Thread-safe implementation using locks for concurrent access.
/// </summary>
public class MultiClickDetector : IMultiClickDetector
{
    private readonly ILogger<MultiClickDetector>? _logger;
    private readonly string _name;
    private readonly object _lock = new();

    private DateTime _lastClickTime = DateTime.MinValue;
    private int _clickCount;
    private CancellationTokenSource? _timerCts;
    private bool _disposed;

    /// <summary>
    /// Default click threshold in milliseconds (800ms for reliable triple-click detection).
    /// </summary>
    public const int DefaultClickThresholdMs = 800;

    /// <summary>
    /// Default debounce time in milliseconds.
    /// </summary>
    public const int DefaultClickDebounceMs = 50;

    /// <summary>
    /// Default delay after key simulation in milliseconds.
    /// </summary>
    public const int KeySimulationDelayMs = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiClickDetector"/> class.
    /// </summary>
    /// <param name="name">Name for logging purposes (e.g., "LEFT", "RIGHT").</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="maxClickCount">Maximum click count to detect (default: 3 for triple-click).</param>
    public MultiClickDetector(string name, ILogger<MultiClickDetector>? logger = null, int maxClickCount = 3)
    {
        _name = name;
        _logger = logger;
        MaxClickCount = maxClickCount;
        ClickThresholdMs = DefaultClickThresholdMs;
        ClickDebounceMs = DefaultClickDebounceMs;
    }

    /// <inheritdoc/>
    public event EventHandler<ClickDetectedEventArgs>? ClickDetected;

    /// <inheritdoc/>
    public int ClickThresholdMs { get; set; }

    /// <inheritdoc/>
    public int ClickDebounceMs { get; set; }

    /// <inheritdoc/>
    public int MaxClickCount { get; set; }

    /// <inheritdoc/>
    public void RegisterClick()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MultiClickDetector));

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;

            // Check if this click is within the threshold window
            if (timeSinceLastClick <= ClickThresholdMs && timeSinceLastClick > ClickDebounceMs)
            {
                _clickCount++;
                _lastClickTime = now;
                _logger?.LogDebug("{Name} click {Count} recorded", _name, _clickCount);

                // Cancel any pending timer
                CancelTimer();

                if (_clickCount >= MaxClickCount)
                {
                    // Max clicks reached - fire immediately
                    var result = GetClickResult(_clickCount);
                    ResetState();

                    _logger?.LogDebug("{Name} {Result} detected (immediate)", _name, result);
                    OnClickDetected(result);
                }
                else
                {
                    // Wait for potential next click
                    StartTimer(_clickCount);
                }
            }
            else
            {
                // First click or timeout - start new sequence
                _clickCount = 1;
                _lastClickTime = now;
                CancelTimer();

                _logger?.LogDebug("{Name} click 1 recorded (new sequence)", _name);
                StartTimer(1);
            }
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (_lock)
        {
            CancelTimer();
            ResetState();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            CancelTimer();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private void StartTimer(int clickCountAtStart)
    {
        _timerCts = new CancellationTokenSource();
        var cts = _timerCts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ClickThresholdMs, cts.Token);

                lock (_lock)
                {
                    if (_disposed || cts.IsCancellationRequested)
                        return;

                    var result = GetClickResult(clickCountAtStart);
                    ResetState();

                    _logger?.LogDebug("{Name} {Result} detected (timer expired)", _name, result);
                    OnClickDetected(result);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled by next click
            }
        });
    }

    private void CancelTimer()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;
    }

    private void ResetState()
    {
        _clickCount = 0;
        _lastClickTime = DateTime.MinValue;
    }

    private static ClickResult GetClickResult(int clickCount) => clickCount switch
    {
        1 => ClickResult.SingleClick,
        2 => ClickResult.DoubleClick,
        >= 3 => ClickResult.TripleClick,
        _ => ClickResult.Pending
    };

    private void OnClickDetected(ClickResult result)
    {
        ClickDetected?.Invoke(this, new ClickDetectedEventArgs(result));
    }
}
