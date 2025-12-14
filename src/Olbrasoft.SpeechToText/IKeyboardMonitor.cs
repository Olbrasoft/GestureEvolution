namespace Olbrasoft.SpeechToText;

/// <summary>
/// Interface for monitoring keyboard events (ISP: read-only operations).
/// Key simulation is handled by IKeySimulator.
/// </summary>
public interface IKeyboardMonitor : IDisposable
{
    /// <summary>
    /// Event raised when a key is pressed.
    /// </summary>
    event EventHandler<KeyEventArgs>? KeyPressed;

    /// <summary>
    /// Event raised when a key is released.
    /// </summary>
    event EventHandler<KeyEventArgs>? KeyReleased;

    /// <summary>
    /// Gets a value indicating whether keyboard monitoring is currently active.
    /// </summary>
    bool IsMonitoring { get; }

    /// <summary>
    /// Starts monitoring keyboard events.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops monitoring keyboard events.
    /// </summary>
    Task StopMonitoringAsync();

    /// <summary>
    /// Gets the current state of CapsLock.
    /// </summary>
    /// <returns>True if CapsLock is ON (LED lit), false if OFF.</returns>
    bool IsCapsLockOn();

    /// <summary>
    /// Gets the current state of ScrollLock.
    /// </summary>
    /// <returns>True if ScrollLock is ON (LED lit), false if OFF.</returns>
    bool IsScrollLockOn();

    /// <summary>
    /// Programmatically raises a KeyReleased event without actually pressing the key.
    /// Used by BluetoothMouseMonitor to trigger DictationWorker without physical key press.
    /// This does NOT change LED state - only raises the event for subscribers.
    /// </summary>
    /// <param name="key">The key to raise event for.</param>
    void RaiseKeyReleasedEvent(KeyCode key);
}
