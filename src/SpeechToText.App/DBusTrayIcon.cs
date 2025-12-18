using System.IO;
using Microsoft.Extensions.Logging;
using Tmds.DBus.SourceGenerator;
using Tmds.DBus.Protocol;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// D-Bus StatusNotifierItem implementation for system tray icon.
/// Uses direct D-Bus communication to bypass GNOME Shell icon caching issues.
/// </summary>
/// <remarks>
/// Based on Avalonia's DBusTrayIconImpl:
/// https://github.com/AvaloniaUI/Avalonia/blob/master/src/Avalonia.FreeDesktop/DBusTrayIconImpl.cs
/// </remarks>
public class DBusTrayIcon : IDisposable
{
    private static int s_instanceId;

    private readonly ILogger<DBusTrayIcon> _logger;
    private readonly SvgIconRenderer _iconRenderer;

    private Connection? _connection;
    private OrgFreedesktopDBusProxy? _dBus;
    private OrgKdeStatusNotifierWatcherProxy? _statusNotifierWatcher;
    private StatusNotifierItemHandler? _sniHandler;
    private PathHandler? _pathHandler;
    private DBusMenuHandler? _menuHandler;
    private PathHandler? _menuPathHandler;

    private IDisposable? _serviceWatchDisposable;

    private string? _sysTrayServiceName;
    private bool _isDisposed;
    private bool _serviceConnected;
    private bool _isVisible = true;

    // Current icon state
    private (int, int, byte[]) _currentIcon = SvgIconRenderer.EmptyPixmap;
    private string _tooltipText = "Speech to Text";

    // Animation support
    private Timer? _animationTimer;
    private string[]? _animationFrames;
    private int _currentFrameIndex;
    private readonly object _animationLock = new();

    public bool IsActive { get; private set; }

    /// <summary>
    /// Event fired when the tray icon is clicked.
    /// </summary>
    public event Action? OnClicked;

    /// <summary>
    /// Event fired when user selects Quit from the context menu.
    /// </summary>
    public event Action? OnQuitRequested;

    /// <summary>
    /// Event fired when user selects About from the context menu.
    /// </summary>
    public event Action? OnAboutRequested;

    public DBusTrayIcon(ILogger<DBusTrayIcon> logger, string iconsPath, int iconSize = 22)
    {
        _logger = logger;
        _iconRenderer = new SvgIconRenderer(logger, iconsPath, iconSize);
    }

    /// <summary>
    /// Initializes the D-Bus connection and registers with StatusNotifierWatcher.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _connection = new Connection(Address.Session!);
            await _connection.ConnectAsync();

            _dBus = new OrgFreedesktopDBusProxy(_connection, "org.freedesktop.DBus", "/org/freedesktop/DBus");

            // Standard paths as per StatusNotifierItem spec
            _pathHandler = new PathHandler("/StatusNotifierItem");
            _sniHandler = new StatusNotifierItemHandler(_connection, _logger, "/MenuBar");
            _sniHandler.ActivationDelegate += () => OnClicked?.Invoke();

            // D-Bus menu handler at /MenuBar
            _menuPathHandler = new PathHandler("/MenuBar");
            _menuHandler = new DBusMenuHandler(_connection, _logger);
            _menuHandler.OnQuitRequested += () => OnQuitRequested?.Invoke();
            _menuHandler.OnAboutRequested += () => OnAboutRequested?.Invoke();

            IsActive = true;

            // Start watching for StatusNotifierWatcher service
            await WatchAsync();

            _logger.LogInformation("DBusTrayIcon initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize DBusTrayIcon");
            IsActive = false;
        }
    }

    private async Task WatchAsync()
    {
        try
        {
            _serviceWatchDisposable = await _dBus!.WatchNameOwnerChangedAsync(
                (exception, change) =>
                {
                    if (exception is null && change.Item1 == "org.kde.StatusNotifierWatcher")
                    {
                        OnNameChange(change.Item1, change.Item3);
                    }
                },
                emitOnCapturedContext: false);

            var nameOwner = await _dBus.GetNameOwnerAsync("org.kde.StatusNotifierWatcher");
            OnNameChange("org.kde.StatusNotifierWatcher", nameOwner);
        }
        catch (DBusException ex) when (ex.ErrorName == "org.freedesktop.DBus.Error.NameHasNoOwner")
        {
            _logger.LogWarning("StatusNotifierWatcher service not available. Tray icon will not be visible.");
        }
        catch (Exception ex)
        {
            _serviceWatchDisposable = null;
            _logger.LogError(ex, "Failed to watch StatusNotifierWatcher service");
        }
    }

    private void OnNameChange(string name, string? newOwner)
    {
        if (_isDisposed || _connection is null || name != "org.kde.StatusNotifierWatcher")
            return;

        if (!_serviceConnected && newOwner is not null)
        {
            _serviceConnected = true;
            _statusNotifierWatcher = new OrgKdeStatusNotifierWatcherProxy(_connection, "org.kde.StatusNotifierWatcher", "/StatusNotifierWatcher");

            DestroyTrayIcon();

            if (_isVisible)
                _ = CreateTrayIconAsync();
        }
        else if (_serviceConnected && newOwner is null)
        {
            DestroyTrayIcon();
            _serviceConnected = false;
        }
    }

    private async Task CreateTrayIconAsync()
    {
        if (_connection is null || !_serviceConnected || _isDisposed || _statusNotifierWatcher is null)
            return;

        try
        {
            // Add SNI handler if needed
            if (_sniHandler!.PathHandler is null)
                _pathHandler!.Add(_sniHandler);

            _connection.RemoveMethodHandler(_pathHandler!.Path);
            _connection.AddMethodHandler(_pathHandler);

            // Add menu handler if needed
            if (_menuHandler!.PathHandler is null)
                _menuPathHandler!.Add(_menuHandler);

            _connection.RemoveMethodHandler(_menuPathHandler!.Path);
            _connection.AddMethodHandler(_menuPathHandler);

            // Register with unique connection name only (issue #62)
            // Using well-known service name causes duplicate detection by some watchers
            _sysTrayServiceName = _connection.UniqueName!;
            await _statusNotifierWatcher.RegisterStatusNotifierItemAsync(_sysTrayServiceName);

            // Set initial state IMMEDIATELY after registration (for fast desktop environments)
            _sniHandler.SetTitleAndTooltip(_tooltipText);
            _sniHandler.SetIcon(_currentIcon);

            _logger.LogInformation("Tray icon registered as {ServiceName}", _sysTrayServiceName);

            // Re-emit signals after delays for GNOME Shell's appindicator extension
            // (it needs extra time to connect and receive the signals)
            _ = Task.Run(async () =>
            {
                // First retry after 100ms
                await Task.Delay(100);
                if (!_isDisposed && _sniHandler?.PathHandler is not null)
                {
                    _sniHandler.SetIcon(_currentIcon);
                }

                // Second retry after 500ms for slower shells
                await Task.Delay(400);
                if (!_isDisposed && _sniHandler?.PathHandler is not null)
                {
                    _sniHandler.SetIcon(_currentIcon);
                    _logger.LogDebug("Re-emitted icon signals for GNOME Shell");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create tray icon");
        }
    }

    private void DestroyTrayIcon()
    {
        if (_connection is null || !_serviceConnected || _isDisposed || _sniHandler is null || _sysTrayServiceName is null)
            return;

        try
        {
            // No ReleaseNameAsync - we use unique connection name, not well-known name (issue #62)
            _pathHandler!.Remove(_sniHandler);
            _connection.RemoveMethodHandler(_pathHandler.Path);

            // Also remove menu handler
            if (_menuHandler is not null && _menuPathHandler is not null)
            {
                _menuPathHandler.Remove(_menuHandler);
                _connection.RemoveMethodHandler(_menuPathHandler.Path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error destroying tray icon");
        }
    }

    /// <summary>
    /// Sets the tray icon from an SVG file name (without path and extension).
    /// </summary>
    /// <param name="iconName">Icon name, e.g., "trigger-ptt"</param>
    public void SetIcon(string iconName)
    {
        if (_isDisposed)
            return;

        try
        {
            var pixmap = _iconRenderer.GetIcon(iconName);
            if (pixmap.HasValue)
            {
                _currentIcon = pixmap.Value;
                // Only call SetIcon if handler is registered on D-Bus (issue #62)
                if (_sniHandler?.PathHandler is not null)
                    _sniHandler.SetIcon(_currentIcon);
                _logger.LogDebug("Set icon: {IconName} ({Width}x{Height})", iconName, _currentIcon.Item1, _currentIcon.Item2);
            }
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "Icon file not found: {IconName}", iconName);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid icon operation: {IconName}", iconName);
        }
    }

    /// <summary>
    /// Sets the attention icon from an SVG file name for animation frames.
    /// Uses NeedsAttention status to force GNOME Shell to refresh.
    /// </summary>
    private void SetAttentionIconInternal(string iconName)
    {
        if (_isDisposed)
            return;

        try
        {
            var pixmap = _iconRenderer.GetIcon(iconName);
            if (pixmap.HasValue && _sniHandler?.PathHandler is not null)
            {
                _sniHandler.SetAttentionIcon(pixmap.Value);
            }
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "Attention icon file not found: {IconName}", iconName);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid attention icon operation: {IconName}", iconName);
        }
    }

    /// <summary>
    /// Sets the tooltip text.
    /// </summary>
    public void SetTooltip(string text)
    {
        if (_isDisposed || text is null)
            return;

        _tooltipText = text;
        // Only call SetTitleAndTooltip if handler is registered on D-Bus (issue #62)
        if (_sniHandler?.PathHandler is not null)
            _sniHandler.SetTitleAndTooltip(text);
    }

    /// <summary>
    /// Starts an icon animation by cycling through the specified frames.
    /// </summary>
    /// <param name="frameNames">Array of icon names (without path/extension) to cycle through</param>
    /// <param name="intervalMs">Interval between frames in milliseconds</param>
    public void StartAnimation(string[] frameNames, int intervalMs = 150)
    {
        if (_isDisposed || frameNames.Length == 0)
            return;

        lock (_animationLock)
        {
            // Stop any existing animation
            StopAnimationInternal();

            // Pre-cache all frames
            _iconRenderer.PreCacheIcons(frameNames);

            _animationFrames = frameNames;
            _currentFrameIndex = 0;

            // Set first frame immediately using animation method with timestamp
            var firstFrame = _iconRenderer.GetIcon(frameNames[0]);
            if (firstFrame.HasValue)
            {
                _currentIcon = firstFrame.Value;
                _sniHandler?.SetAnimationFrame(_currentIcon, _currentFrameIndex);
            }

            // Start timer for subsequent frames
            _animationTimer = new Timer(AnimationCallback, null, intervalMs, intervalMs);
            _logger.LogDebug("Animation started with {FrameCount} frames, interval {Interval}ms", frameNames.Length, intervalMs);
        }
    }

    /// <summary>
    /// Stops the current icon animation.
    /// </summary>
    public void StopAnimation()
    {
        lock (_animationLock)
        {
            StopAnimationInternal();
        }
    }

    private void StopAnimationInternal()
    {
        _animationTimer?.Dispose();
        _animationTimer = null;
        _animationFrames = null;
        _currentFrameIndex = 0;
    }

    private void AnimationCallback(object? state)
    {
        if (_isDisposed)
            return;

        lock (_animationLock)
        {
            if (_animationFrames is null || _animationFrames.Length == 0)
                return;

            _currentFrameIndex = (_currentFrameIndex + 1) % _animationFrames.Length;
            var frameName = _animationFrames[_currentFrameIndex];

            // Get icon from renderer (cached or newly loaded)
            var pixmap = _iconRenderer.GetIcon(frameName);
            if (pixmap.HasValue)
            {
                _currentIcon = pixmap.Value;
                _sniHandler?.SetAnimationFrame(_currentIcon, _currentFrameIndex);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        IsActive = false;

        StopAnimation();
        DestroyTrayIcon();

        // Clean up menu handler
        if (_menuHandler is not null && _menuPathHandler is not null)
        {
            _menuPathHandler.Remove(_menuHandler);
            _connection?.RemoveMethodHandler(_menuPathHandler.Path);
        }

        _serviceWatchDisposable?.Dispose();
        _connection?.Dispose();

        _iconRenderer.ClearCache();

        _logger.LogInformation("DBusTrayIcon disposed");
    }
}
