using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.Actions;

namespace Olbrasoft.SpeechToText;

/// <summary>
/// Monitors Bluetooth mouse button events and executes configured actions.
/// Uses EVIOCGRAB to grab device exclusively - events won't propagate to system.
/// Designed for Microsoft BluetoothMouse3600 used as a remote push-to-talk trigger.
/// </summary>
/// <remarks>
/// Button mappings:
/// - LEFT: Single=CapsLock, Double=None, Triple=OpenCode
/// - MIDDLE: Single=Enter, Double=Chrome, Triple=Ctrl+C
/// - RIGHT: Single=ESC, Double=Ctrl+Shift+V, Triple=Claude
/// </remarks>
public class BluetoothMouseMonitor : MouseMonitorBase
{
    // Button click handlers
    private readonly ButtonClickHandler _leftButtonHandler;
    private readonly ButtonClickHandler _middleButtonHandler;
    private readonly ButtonClickHandler _rightButtonHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="BluetoothMouseMonitor"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="keyboardMonitor">Keyboard monitor for raising key events.</param>
    /// <param name="keySimulator">Key simulator for simulating key presses.</param>
    /// <param name="deviceNamePattern">Device name pattern to search for (default: "BluetoothMouse3600").</param>
    /// <param name="leftTripleClickCommand">Command to execute on LEFT triple-click (optional).</param>
    public BluetoothMouseMonitor(
        ILogger<BluetoothMouseMonitor> logger,
        IKeyboardMonitor keyboardMonitor,
        IKeySimulator keySimulator,
        string deviceNamePattern = "BluetoothMouse3600",
        string? leftTripleClickCommand = null)
        : this(logger, keyboardMonitor, keySimulator, new InputDeviceDiscovery(), deviceNamePattern, leftTripleClickCommand)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BluetoothMouseMonitor"/> class with dependency injection.
    /// </summary>
    internal BluetoothMouseMonitor(
        ILogger<BluetoothMouseMonitor> logger,
        IKeyboardMonitor keyboardMonitor,
        IKeySimulator keySimulator,
        IInputDeviceDiscovery deviceDiscovery,
        string deviceNamePattern,
        string? leftTripleClickCommand)
        : base(logger, deviceDiscovery, deviceNamePattern, "Bluetooth mouse")
    {
        ArgumentNullException.ThrowIfNull(keyboardMonitor);
        ArgumentNullException.ThrowIfNull(keySimulator);

        // Configure LEFT button: Single=CapsLock, Double=None, Triple=OpenCode
        _leftButtonHandler = new ButtonClickHandler(
            "LEFT",
            new KeyPressAction(keySimulator, keyboardMonitor, KeyCode.CapsLock, "CapsLock (toggle recording)"),
            NoAction.Instance,
            string.IsNullOrEmpty(leftTripleClickCommand)
                ? NoAction.Instance
                : new ShellCommandAction(leftTripleClickCommand, $"Command: {leftTripleClickCommand}"),
            logger,
            maxClickCount: 3);

        // Configure MIDDLE button: Single=Enter, Double=Chrome, Triple=Ctrl+C
        _middleButtonHandler = new ButtonClickHandler(
            "MIDDLE",
            new KeyPressAction(keySimulator, keyboardMonitor, KeyCode.Enter, "Enter"),
            new ShellCommandAction("~/.local/bin/focus-chrome.sh", "Focus Chrome"),
            new KeyComboAction(keySimulator, KeyCode.LeftControl, KeyCode.C, "Ctrl+C (copy)"),
            logger,
            maxClickCount: 3);

        // Configure RIGHT button: Single=ESC, Double=Ctrl+Shift+V, Triple=Claude
        _rightButtonHandler = new ButtonClickHandler(
            "RIGHT",
            new KeyPressAction(keySimulator, keyboardMonitor, KeyCode.Escape, "ESC (cancel transcription)", raiseReleaseEvent: true),
            new KeyComboWithTwoModifiersAction(keySimulator, KeyCode.LeftControl, KeyCode.LeftShift, KeyCode.V, "Ctrl+Shift+V (terminal paste)"),
            new ShellCommandAction("~/.local/bin/focus-claude.sh", "Focus Claude"),
            logger,
            maxClickCount: 3);
    }

    /// <inheritdoc/>
    protected override string? FindDevice()
    {
        return DeviceDiscovery.FindMouseDevice(DeviceNamePattern);
    }

    /// <inheritdoc/>
    protected override void HandleButtonPress(MouseButton button)
    {
        switch (button)
        {
            case MouseButton.Left:
                _leftButtonHandler.RegisterClick();
                break;

            case MouseButton.Middle:
                _middleButtonHandler.RegisterClick();
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
        _middleButtonHandler.Dispose();
        _rightButtonHandler.Dispose();
    }
}
