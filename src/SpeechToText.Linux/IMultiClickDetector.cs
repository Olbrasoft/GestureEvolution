namespace Olbrasoft.SpeechToText;

/// <summary>
/// Result of multi-click detection.
/// </summary>
public enum ClickResult
{
    /// <summary>
    /// Waiting for more clicks or timer expiration.
    /// </summary>
    Pending,

    /// <summary>
    /// Single click detected (after timeout).
    /// </summary>
    SingleClick,

    /// <summary>
    /// Double click detected.
    /// </summary>
    DoubleClick,

    /// <summary>
    /// Triple click detected.
    /// </summary>
    TripleClick
}

/// <summary>
/// Event args for click detection results.
/// </summary>
public class ClickDetectedEventArgs : EventArgs
{
    public ClickDetectedEventArgs(ClickResult result)
    {
        Result = result;
    }

    /// <summary>
    /// The detected click result.
    /// </summary>
    public ClickResult Result { get; }
}

/// <summary>
/// Interface for detecting multi-click patterns (single, double, triple click).
/// Implements timing-based click detection with debounce support.
/// </summary>
public interface IMultiClickDetector : IDisposable
{
    /// <summary>
    /// Event raised when a click pattern is detected.
    /// </summary>
    event EventHandler<ClickDetectedEventArgs>? ClickDetected;

    /// <summary>
    /// Gets or sets the maximum time between clicks for multi-click detection (ms).
    /// </summary>
    int ClickThresholdMs { get; set; }

    /// <summary>
    /// Gets or sets the minimum time between clicks for debounce (ms).
    /// </summary>
    int ClickDebounceMs { get; set; }

    /// <summary>
    /// Gets or sets the maximum click count to detect (2 for double-click, 3 for triple-click).
    /// </summary>
    int MaxClickCount { get; set; }

    /// <summary>
    /// Registers a click event. Returns immediately; fires ClickDetected event when pattern is determined.
    /// </summary>
    void RegisterClick();

    /// <summary>
    /// Resets the detector state.
    /// </summary>
    void Reset();
}
