using Microsoft.Extensions.Logging;
using Tmds.DBus.SourceGenerator;
using Tmds.DBus.Protocol;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// Separate D-Bus StatusNotifierItem for animated icon during transcription.
/// Shows/hides as a second tray icon to work around GNOME Shell caching.
/// </summary>
public class DBusAnimatedIcon : IDisposable
{
    private static int s_instanceId = 1000; // Different range from main icon

    private readonly ILogger _logger;
    private readonly SvgIconRenderer _iconRenderer;
    private readonly string[] _frameNames;
    private readonly int _intervalMs;

    private Connection? _connection;
    private OrgFreedesktopDBusProxy? _dBus;
    private OrgKdeStatusNotifierWatcherProxy? _statusNotifierWatcher;
    private AnimatedIconHandler? _sniHandler;
    private PathHandler? _pathHandler;
    private IDisposable? _serviceWatchDisposable;

    private string? _sysTrayServiceName;
    private bool _isDisposed;
    private bool _serviceConnected;
    private bool _isVisible;

    // Animation
    private Timer? _animationTimer;
    private int _currentFrameIndex;
    private readonly object _animationLock = new();

    public bool IsActive { get; private set; }

    public DBusAnimatedIcon(ILogger logger, string iconsPath, string[] frameNames, int iconSize = 22, int intervalMs = 150)
    {
        _logger = logger;
        _iconRenderer = new SvgIconRenderer(logger, iconsPath, iconSize);
        _frameNames = frameNames;
        _intervalMs = intervalMs;
    }

    /// <summary>
    /// Initializes the animated icon (pre-caches frames only, D-Bus connection is created lazily in ShowAsync).
    /// </summary>
    public Task InitializeAsync()
    {
        // Pre-cache all animation frames (no D-Bus connection yet - issue #62)
        // D-Bus connection is created lazily in ShowAsync() to avoid registering
        // a second tray icon on startup
        _logger.LogDebug("Loading animation frames from: {IconsPath}", _iconRenderer.IconsPath);
        _iconRenderer.PreCacheIcons(_frameNames);

        IsActive = true;
        _logger.LogInformation("DBusAnimatedIcon initialized with {CachedCount}/{TotalCount} frames cached (D-Bus lazy)",
            _iconRenderer.CacheCount, _frameNames.Length);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates D-Bus connection and sets up StatusNotifierWatcher.
    /// Called lazily from ShowAsync().
    /// </summary>
    private async Task EnsureConnectedAsync()
    {
        if (_connection is not null)
            return;

        _connection = new Connection(Address.Session!);
        await _connection.ConnectAsync();

        _dBus = new OrgFreedesktopDBusProxy(_connection, "org.freedesktop.DBus", "/org/freedesktop/DBus");

        // Standard path as per StatusNotifierItem spec
        _pathHandler = new PathHandler("/StatusNotifierItem");
        _sniHandler = new AnimatedIconHandler(_connection, _logger);

        // Watch for StatusNotifierWatcher
        await WatchAsync();

        _logger.LogDebug("DBusAnimatedIcon D-Bus connection established");
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
            _logger.LogWarning("StatusNotifierWatcher not available for animated icon");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to watch StatusNotifierWatcher");
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
        }
        else if (_serviceConnected && newOwner is null)
        {
            Hide();
            _serviceConnected = false;
        }
    }

    /// <summary>
    /// Shows the animated icon and starts animation.
    /// </summary>
    public async Task ShowAsync()
    {
        if (_isDisposed || _isVisible)
        {
            _logger.LogDebug("ShowAsync skipped: disposed={Disposed}, visible={Visible}", _isDisposed, _isVisible);
            return;
        }

        try
        {
            // Lazy D-Bus connection initialization (issue #62)
            await EnsureConnectedAsync();

            if (!_serviceConnected || _statusNotifierWatcher is null)
            {
                _logger.LogWarning("ShowAsync: StatusNotifierWatcher not available");
                return;
            }

            // Add handler if needed
            if (_sniHandler!.PathHandler is null)
                _pathHandler!.Add(_sniHandler);

            _connection!.RemoveMethodHandler(_pathHandler!.Path);
            _connection.AddMethodHandler(_pathHandler);

            // Register with unique connection name only (issue #62)
            // Using well-known service name causes duplicate detection by some watchers
            _sysTrayServiceName = _connection.UniqueName!;

            // Set first frame BEFORE registration so GNOME Shell reads valid pixmap
            _currentFrameIndex = 0;
            SetCurrentFrame();

            await _statusNotifierWatcher.RegisterStatusNotifierItemAsync(_sysTrayServiceName);

            _isVisible = true;
            _animationTimer = new Timer(AnimationCallback, null, _intervalMs, _intervalMs);

            _logger.LogInformation("Animated icon shown as {ServiceName}", _sysTrayServiceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show animated icon");
        }
    }

    /// <summary>
    /// Hides the animated icon and stops animation.
    /// </summary>
    public void Hide()
    {
        if (_isDisposed || !_isVisible)
            return;

        lock (_animationLock)
        {
            _animationTimer?.Dispose();
            _animationTimer = null;
        }

        try
        {
            // Signal GNOME Shell to hide the icon BEFORE removing handler (issue #77)
            if (_sniHandler is not null)
            {
                _sniHandler.SetStatus("Passive");
            }

            // No ReleaseNameAsync - we use unique connection name, not well-known name (issue #62)
            if (_sniHandler is not null && _pathHandler is not null)
            {
                _pathHandler.Remove(_sniHandler);
                _connection?.RemoveMethodHandler(_pathHandler.Path);
            }

            _isVisible = false;
            _logger.LogDebug("Animated icon hidden");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error hiding animated icon");
        }
    }

    private void AnimationCallback(object? state)
    {
        if (_isDisposed || !_isVisible)
            return;

        lock (_animationLock)
        {
            _currentFrameIndex = (_currentFrameIndex + 1) % _frameNames.Length;
            SetCurrentFrame();
        }
    }

    private void SetCurrentFrame()
    {
        if (_frameNames.Length == 0 || _sniHandler is null)
        {
            _logger.LogDebug("SetCurrentFrame skipped: frames={FrameCount}, handler={HasHandler}",
                _frameNames.Length, _sniHandler is not null);
            return;
        }

        var frameName = _frameNames[_currentFrameIndex];
        var pixmap = _iconRenderer.GetIcon(frameName);
        if (pixmap.HasValue)
        {
            _sniHandler.SetIcon(pixmap.Value);
            _logger.LogDebug("Set frame {FrameIndex}: {FrameName}", _currentFrameIndex, frameName);
        }
        else
        {
            _logger.LogWarning("Frame not in cache: {FrameName} (cache has {CacheCount} items)",
                frameName, _iconRenderer.CacheCount);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        IsActive = false;

        Hide();
        _serviceWatchDisposable?.Dispose();
        _connection?.Dispose();
        _iconRenderer.ClearCache();

        _logger.LogInformation("DBusAnimatedIcon disposed");
    }
}

/// <summary>
/// D-Bus handler for animated icon.
/// </summary>
internal class AnimatedIconHandler : OrgKdeStatusNotifierItemHandler
{
    private readonly ILogger _logger;

    public AnimatedIconHandler(Connection connection, ILogger logger)
    {
        Connection = connection;
        _logger = logger;

        Category = "ApplicationStatus";
        Id = "ptt-animation";
        Title = "Transcribing...";
        Status = "Active";
        IconName = "";
        IconPixmap = Array.Empty<(int, int, byte[])>();
        OverlayIconName = "";
        OverlayIconPixmap = Array.Empty<(int, int, byte[])>();
        AttentionIconName = "";
        AttentionIconPixmap = Array.Empty<(int, int, byte[])>();
        AttentionMovieName = "";
        IconThemePath = "";
        Menu = new ObjectPath("/MenuBar");
        ItemIsMenu = false;
        ToolTip = ("", Array.Empty<(int, int, byte[])>(), "Transcribing...", "");
        WindowId = 0;
    }

    public override Connection Connection { get; }

    protected override ValueTask OnContextMenuAsync(Message message, int x, int y)
        => ValueTask.CompletedTask;

    protected override ValueTask OnActivateAsync(Message message, int x, int y)
        => ValueTask.CompletedTask;

    protected override ValueTask OnSecondaryActivateAsync(Message message, int x, int y)
        => ValueTask.CompletedTask;

    protected override ValueTask OnScrollAsync(Message message, int delta, string orientation)
        => ValueTask.CompletedTask;

    public void SetIcon((int, int, byte[]) dbusPixmap)
    {
        IconPixmap = new[] { dbusPixmap };
        IconName = "";  // Clear icon name to force pixmap usage
        Status = "Active";

        // Emit signals to notify the tray about the change
        EmitNewIcon();
        EmitNewStatus(Status);
    }

    /// <summary>
    /// Sets the icon status and emits D-Bus signal to notify GNOME Shell.
    /// Use "Passive" to hide the icon, "Active" to show it.
    /// </summary>
    public void SetStatus(string status)
    {
        Status = status;
        EmitNewStatus(status);
    }
}
