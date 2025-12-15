using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText;

/// <summary>
/// Base class for mouse monitors that use evdev for device input.
/// Provides common functionality for device discovery, exclusive grabbing, and event monitoring.
/// </summary>
/// <remarks>
/// Subclasses must implement:
/// - ConfigureButtonHandlers: Set up button click handlers
/// - HandleButtonPress: Process button press events
/// - FindDevice: Find the appropriate device path
/// - DisposeButtonHandlers: Clean up button handlers
/// </remarks>
public abstract class MouseMonitorBase : IDisposable
{
    private readonly ILogger _logger;
    private readonly IInputDeviceDiscovery _deviceDiscovery;
    private readonly string _deviceNamePattern;
    private readonly string _monitorTypeName;

    private FileStream? _deviceStream;
    private bool _isMonitoring;
    private bool _disposed;
    private bool _deviceGrabbed;
    private Task? _reconnectTask;
    private CancellationTokenSource? _cts;

    // P/Invoke for ioctl to grab/ungrab the device
    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, uint request, int value);

    /// <summary>
    /// Initializes a new instance of the <see cref="MouseMonitorBase"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="deviceDiscovery">Device discovery service.</param>
    /// <param name="deviceNamePattern">Device name pattern to search for.</param>
    /// <param name="monitorTypeName">Name of this monitor type for logging (e.g., "Bluetooth mouse", "USB mouse").</param>
    protected MouseMonitorBase(
        ILogger logger,
        IInputDeviceDiscovery deviceDiscovery,
        string deviceNamePattern,
        string monitorTypeName)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deviceDiscovery = deviceDiscovery ?? throw new ArgumentNullException(nameof(deviceDiscovery));
        _deviceNamePattern = deviceNamePattern;
        _monitorTypeName = monitorTypeName;
    }

    /// <summary>
    /// Gets the logger instance.
    /// </summary>
    protected ILogger Logger => _logger;

    /// <summary>
    /// Gets the device discovery service.
    /// </summary>
    protected IInputDeviceDiscovery DeviceDiscovery => _deviceDiscovery;

    /// <summary>
    /// Gets the device name pattern.
    /// </summary>
    protected string DeviceNamePattern => _deviceNamePattern;

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
    /// Finds the device path for this monitor type.
    /// </summary>
    /// <returns>Device path if found, null otherwise.</returns>
    protected abstract string? FindDevice();

    /// <summary>
    /// Handles a button press event by registering clicks with the appropriate handler.
    /// </summary>
    /// <param name="button">The mouse button that was pressed.</param>
    protected abstract void HandleButtonPress(MouseButton button);

    /// <summary>
    /// Disposes the button handlers.
    /// </summary>
    protected abstract void DisposeButtonHandlers();

    /// <summary>
    /// Starts monitoring mouse button events.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (_isMonitoring)
        {
            _logger.LogWarning("{MonitorType} monitoring is already active", _monitorTypeName);
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isMonitoring = true;

        _reconnectTask = Task.Run(() => ReconnectLoopAsync(_cts.Token), _cts.Token);

        _logger.LogInformation("{MonitorType} monitoring started (looking for {Pattern})", _monitorTypeName, _deviceNamePattern);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops monitoring mouse button events.
    /// </summary>
    public async Task StopMonitoringAsync()
    {
        if (!_isMonitoring)
        {
            _logger.LogWarning("{MonitorType} monitoring is not active", _monitorTypeName);
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
            _logger.LogInformation("{MonitorType} monitoring stopped", _monitorTypeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping {MonitorType} monitoring", _monitorTypeName);
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
                var devicePath = FindDevice();

                if (devicePath == null)
                {
                    if (attempts == 0 || attempts % EvdevConstants.LogIntervalAttempts == 0)
                    {
                        _logger.LogInformation("{MonitorType} '{Pattern}' not found, waiting for connection...", _monitorTypeName, _deviceNamePattern);
                    }
                    attempts++;
                    await Task.Delay(EvdevConstants.DefaultReconnectIntervalMs, cancellationToken);
                    continue;
                }

                attempts = 0;

                if (await TryOpenDeviceAsync(devicePath))
                {
                    _logger.LogInformation("Connected to {MonitorType}: {DevicePath}", _monitorTypeName, devicePath);
                    await MonitorEventsAsync(cancellationToken);
                    _logger.LogWarning("{MonitorType} disconnected, will attempt reconnection...", _monitorTypeName);
                    CloseDevice();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in {MonitorType} reconnect loop", _monitorTypeName);
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
                _logger.LogInformation("Device GRABBED exclusively - {MonitorType} events will NOT propagate to system", _monitorTypeName);
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogWarning("Failed to grab device exclusively (error: {Error}). {MonitorType} events will propagate to system!", error, _monitorTypeName);
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
            _logger.LogError(ex, "Error reading {MonitorType} events", _monitorTypeName);
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
            EvdevConstants.BTN_MIDDLE => MouseButton.Middle,
            _ => MouseButton.Unknown
        };

        if (button == MouseButton.Unknown)
            return Task.CompletedTask;

        var eventArgs = new MouseButtonEventArgs(button, code, value == EvdevConstants.KEY_PRESS, DateTime.UtcNow);

        if (value == EvdevConstants.KEY_PRESS)
        {
            _logger.LogDebug("{MonitorType} button pressed: {Button}", _monitorTypeName, button);
            ButtonPressed?.Invoke(this, eventArgs);
            HandleButtonPress(button);
        }
        else
        {
            _logger.LogDebug("{MonitorType} button released: {Button}", _monitorTypeName, button);
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

        DisposeButtonHandlers();
        _cts?.Dispose();
        CloseDevice();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
