using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.Actions;

namespace Olbrasoft.SpeechToText;

/// <summary>
/// Monitors USB Optical Mouse button events and executes configured actions.
/// Uses EVIOCGRAB to grab device exclusively - events won't propagate to system.
/// Designed for Logitech USB Optical Mouse used as a secondary push-to-talk trigger.
/// </summary>
/// <remarks>
/// Button mappings:
/// - LEFT: Single=CapsLock, Double=ESC
/// - RIGHT: Single=None, Double=Ctrl+Shift+V, Triple=Ctrl+C
/// NOTE: Does NOT grab Logitech G203 LIGHTSYNC Gaming Mouse (main mouse).
/// </remarks>
public class UsbMouseMonitor : MouseMonitorBase
{
    private readonly string[] _excludedDevices;

    // Button click handlers
    private readonly ButtonClickHandler _leftButtonHandler;
    private readonly ButtonClickHandler _rightButtonHandler;

    /// <summary>
    /// Default device to exclude (main mouse).
    /// </summary>
    public const string DefaultExcludedDevice = "G203 LIGHTSYNC";

    /// <summary>
    /// Initializes a new instance of the <see cref="UsbMouseMonitor"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="keyboardMonitor">Keyboard monitor for raising key events.</param>
    /// <param name="keySimulator">Key simulator for simulating key presses.</param>
    /// <param name="deviceNamePattern">Device name pattern to search for (default: "USB Optical Mouse").</param>
    public UsbMouseMonitor(
        ILogger<UsbMouseMonitor> logger,
        IKeyboardMonitor keyboardMonitor,
        IKeySimulator keySimulator,
        string deviceNamePattern = "USB Optical Mouse")
        : this(logger, keyboardMonitor, keySimulator, new InputDeviceDiscovery(), deviceNamePattern, [DefaultExcludedDevice])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UsbMouseMonitor"/> class with dependency injection.
    /// </summary>
    internal UsbMouseMonitor(
        ILogger<UsbMouseMonitor> logger,
        IKeyboardMonitor keyboardMonitor,
        IKeySimulator keySimulator,
        IInputDeviceDiscovery deviceDiscovery,
        string deviceNamePattern,
        string[] excludedDevices)
        : base(logger, deviceDiscovery, deviceNamePattern, "USB mouse")
    {
        ArgumentNullException.ThrowIfNull(keyboardMonitor);
        ArgumentNullException.ThrowIfNull(keySimulator);
        _excludedDevices = excludedDevices;

        // Configure LEFT button: Single=CapsLock, Double=ESC (no triple-click)
        _leftButtonHandler = new ButtonClickHandler(
            "USB LEFT",
            new KeyPressAction(keySimulator, keyboardMonitor, KeyCode.CapsLock, "CapsLock (toggle recording)"),
            new KeyPressAction(keySimulator, keyboardMonitor, KeyCode.Escape, "ESC (cancel transcription)", raiseReleaseEvent: true),
            NoAction.Instance, // No triple-click action for USB mouse
            logger,
            maxClickCount: 2);

        // Configure RIGHT button: Single=None, Double=Ctrl+Shift+V, Triple=Ctrl+C
        _rightButtonHandler = new ButtonClickHandler(
            "USB RIGHT",
            NoAction.Instance,
            new KeyComboWithTwoModifiersAction(keySimulator, KeyCode.LeftControl, KeyCode.LeftShift, KeyCode.V, "Ctrl+Shift+V (terminal paste)"),
            new KeyComboAction(keySimulator, KeyCode.LeftControl, KeyCode.C, "Ctrl+C (copy)"),
            logger,
            maxClickCount: 3);
    }

    /// <inheritdoc/>
    protected override string? FindDevice()
    {
        return DeviceDiscovery.FindMouseDevice(DeviceNamePattern, _excludedDevices);
    }

    /// <inheritdoc/>
    protected override void HandleButtonPress(MouseButton button)
    {
        switch (button)
        {
            case MouseButton.Left:
                _leftButtonHandler.RegisterClick();
                break;

            case MouseButton.Right:
                _rightButtonHandler.RegisterClick();
                break;
        }
    }

    /// <inheritdoc/>
    protected override void DisposeButtonHandlers()
    {
        _leftButtonHandler.Dispose();
        _rightButtonHandler.Dispose();
    }
}
