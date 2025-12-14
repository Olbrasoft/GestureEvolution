using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

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
public class UsbMouseMonitor : IDisposable
{
    private readonly ILogger<UsbMouseMonitor> _logger;
    private readonly IKeyboardMonitor _keyboardMonitor;
    private readonly IInputDeviceDiscovery _deviceDiscovery;
    private readonly string _deviceNamePattern;
    private readonly string[] _excludedDevices;

    private FileStream? _deviceStream;
    private bool _isMonitoring;
    private bool _disposed;
    private bool _deviceGrabbed;
    private Task? _reconnectTask;
    private CancellationTokenSource? _cts;

    // Button click handlers
    private readonly ButtonClickHandler _leftButtonHandler;
    private readonly ButtonClickHandler _rightButtonHandler;

    /// <summary>
    /// Default device to exclude (main mouse).
    /// </summary>
    public const string DefaultExcludedDevice = "G203 LIGHTSYNC";

    // P/Invoke for ioctl to grab/ungrab the device
    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, uint request, int value);

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
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _keyboardMonitor = keyboardMonitor ?? throw new ArgumentNullException(nameof(keyboardMonitor));
        ArgumentNullException.ThrowIfNull(keySimulator);
        _deviceDiscovery = deviceDiscovery ?? throw new ArgumentNullException(nameof(deviceDiscovery));
        _deviceNamePattern = deviceNamePattern;
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

    /// <summary>
    /// Event raised when a mouse button is pressed.
    /// </summary>
    public event EventHandler<MouseButtonEventArgs>? ButtonPressed;

    /// <summary>
    /// Event raised when a mouse button is released.
    /// </summary>
    public event EventHandler<MouseButtonEventArgs>? ButtonReleased;

    /// <summary>
    /// Gets a value indicating whether mouse monitoring is currently active.
    /// </summary>
    public bool IsMonitoring => _isMonitoring;

    /// <summary>
    /// Gets a value indicating whether the device is currently grabbed exclusively.
    /// </summary>
    public bool IsDeviceGrabbed => _deviceGrabbed;

    /// <summary>
    /// Starts monitoring USB mouse button events.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UsbMouseMonitor));

        if (_isMonitoring)
        {
            _logger.LogWarning("USB mouse monitoring is already active");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isMonitoring = true;

        _reconnectTask = Task.Run(() => ReconnectLoopAsync(_cts.Token), _cts.Token);

        _logger.LogInformation("USB mouse monitoring started (looking for {Pattern})", _deviceNamePattern);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops monitoring USB mouse button events.
    /// </summary>
    public async Task StopMonitoringAsync()
    {
        if (!_isMonitoring)
        {
            _logger.LogWarning("USB mouse monitoring is not active");
            return;
        }

        try
        {
            _isMonitoring = false;
            _cts?.Cancel();

            if (_reconnectTask != null)
            {
                try
                {
                    await _reconnectTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                }
            }

            CloseDevice();
            _logger.LogInformation("USB mouse monitoring stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping USB mouse monitoring");
            throw;
        }
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        int attempts = 0;

        while (_isMonitoring && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var devicePath = _deviceDiscovery.FindMouseDevice(_deviceNamePattern, _excludedDevices);

                if (devicePath == null)
                {
                    if (attempts == 0 || attempts % EvdevConstants.LogIntervalAttempts == 0)
                    {
                        _logger.LogInformation("USB mouse '{Pattern}' not found, waiting for connection...", _deviceNamePattern);
                    }
                    attempts++;
                    await Task.Delay(EvdevConstants.DefaultReconnectIntervalMs, cancellationToken);
                    continue;
                }

                attempts = 0;

                if (await TryOpenDeviceAsync(devicePath))
                {
                    _logger.LogInformation("Connected to USB mouse: {DevicePath}", devicePath);
                    await MonitorEventsAsync(cancellationToken);
                    _logger.LogWarning("USB mouse disconnected, will attempt reconnection...");
                    CloseDevice();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in USB mouse reconnect loop");
                CloseDevice();
                await Task.Delay(EvdevConstants.DefaultReconnectIntervalMs, cancellationToken);
            }
        }
    }

    private Task<bool> TryOpenDeviceAsync(string devicePath)
    {
        try
        {
            _deviceStream = new FileStream(
                devicePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: EvdevConstants.InputEventSize,
                useAsync: false);

            int fd = (int)_deviceStream.SafeFileHandle.DangerousGetHandle();
            int result = ioctl(fd, EvdevConstants.EVIOCGRAB, 1);

            if (result == 0)
            {
                _deviceGrabbed = true;
                _logger.LogInformation("Device GRABBED exclusively - USB mouse events will NOT propagate to system");
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogWarning("Failed to grab device exclusively (error: {Error}). USB mouse events will propagate to system!", error);
                _deviceGrabbed = false;
            }

            return Task.FromResult(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Permission denied. Add user to 'input' group: sudo usermod -a -G input $USER");
            return Task.FromResult(false);
        }
        catch (FileNotFoundException)
        {
            _logger.LogDebug("Device not found: {DevicePath}", devicePath);
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open device: {DevicePath}", devicePath);
            return Task.FromResult(false);
        }
    }

    private void CloseDevice()
    {
        if (_deviceStream == null)
            return;

        try
        {
            if (_deviceGrabbed)
            {
                int fd = (int)_deviceStream.SafeFileHandle.DangerousGetHandle();
                ioctl(fd, EvdevConstants.EVIOCGRAB, 0);
                _deviceGrabbed = false;
                _logger.LogDebug("Device ungrabbed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error ungrabbing device");
        }

        try
        {
            _deviceStream.Dispose();
        }
        catch { }

        _deviceStream = null;
    }

    private async Task MonitorEventsAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[EvdevConstants.InputEventSize];

        try
        {
            while (_isMonitoring && !cancellationToken.IsCancellationRequested && _deviceStream != null)
            {
                int bytesRead;
                try
                {
                    bytesRead = _deviceStream.Read(buffer, 0, EvdevConstants.InputEventSize);
                }
                catch (IOException)
                {
                    _logger.LogDebug("IOException during read - device likely disconnected");
                    break;
                }

                if (bytesRead != EvdevConstants.InputEventSize)
                {
                    _logger.LogWarning("Incomplete event data received: {BytesRead} bytes", bytesRead);
                    break;
                }

                await HandleEventAsync(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Event monitoring cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading USB mouse events");
        }
    }

    private Task HandleEventAsync(byte[] buffer)
    {
        var (type, code, value) = ParseInputEvent(buffer);

        if (type != EvdevConstants.EV_KEY)
            return Task.CompletedTask;

        if (value != EvdevConstants.KEY_PRESS && value != EvdevConstants.KEY_RELEASE)
            return Task.CompletedTask;

        var button = code switch
        {
            EvdevConstants.BTN_LEFT => MouseButton.Left,
            EvdevConstants.BTN_RIGHT => MouseButton.Right,
            _ => MouseButton.Unknown
        };

        if (button == MouseButton.Unknown)
            return Task.CompletedTask;

        var eventArgs = new MouseButtonEventArgs(button, code, value == EvdevConstants.KEY_PRESS, DateTime.UtcNow);

        if (value == EvdevConstants.KEY_PRESS)
        {
            _logger.LogDebug("USB mouse button pressed: {Button}", button);
            ButtonPressed?.Invoke(this, eventArgs);

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
        else
        {
            _logger.LogDebug("USB mouse button released: {Button}", button);
            ButtonReleased?.Invoke(this, eventArgs);
        }

        return Task.CompletedTask;
    }

    private static (ushort type, ushort code, int value) ParseInputEvent(byte[] buffer)
    {
        int offset = EvdevConstants.TimevalOffset;
        ushort type = BitConverter.ToUInt16(buffer, offset);
        offset += 2;
        ushort code = BitConverter.ToUInt16(buffer, offset);
        offset += 2;
        int value = BitConverter.ToInt32(buffer, offset);

        return (type, code, value);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        if (_isMonitoring)
        {
            StopMonitoringAsync().GetAwaiter().GetResult();
        }

        _leftButtonHandler.Dispose();
        _rightButtonHandler.Dispose();
        _cts?.Dispose();
        CloseDevice();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
