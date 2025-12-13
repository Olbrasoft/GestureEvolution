using Microsoft.Extensions.Logging;
using Tmds.DBus.SourceGenerator;
using SkiaSharp;
using Svg.Skia;
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
    private static readonly (int, int, byte[]) EmptyPixmap = (1, 1, new byte[] { 255, 0, 0, 0 });

    private readonly ILogger<DBusTrayIcon> _logger;
    private readonly string _iconsPath;
    private readonly int _iconSize;

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
    private (int, int, byte[]) _currentIcon = EmptyPixmap;
    private string _tooltipText = "Speech to Text";

    // Icon cache to avoid re-rendering SVGs
    private readonly Dictionary<string, (int, int, byte[])> _iconCache = new();

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
        _iconsPath = iconsPath;
        _iconSize = iconSize;
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

            _pathHandler = new PathHandler("/StatusNotifierItem");
            _sniHandler = new StatusNotifierItemHandler(_connection, _logger);
            _sniHandler.ActivationDelegate += () => OnClicked?.Invoke();

            _pathHandler.Add(_sniHandler);
            _connection.AddMethodHandler(_pathHandler);

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
            var pid = Environment.ProcessId;
            var tid = Interlocked.Increment(ref s_instanceId);

            // Re-add handler if needed
            if (_sniHandler!.PathHandler is null)
                _pathHandler!.Add(_sniHandler);

            _connection.RemoveMethodHandler(_pathHandler!.Path);
            _connection.AddMethodHandler(_pathHandler);

            _sysTrayServiceName = $"org.kde.StatusNotifierItem-{pid}-{tid}";
            await _dBus!.RequestNameAsync(_sysTrayServiceName, 0);
            await _statusNotifierWatcher.RegisterStatusNotifierItemAsync(_sysTrayServiceName);

            // Set initial state
            _sniHandler.SetTitleAndTooltip(_tooltipText);
            _sniHandler.SetIcon(_currentIcon);

            _logger.LogInformation("Tray icon registered as {ServiceName}", _sysTrayServiceName);
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
            _ = _dBus!.ReleaseNameAsync(_sysTrayServiceName);
            _pathHandler!.Remove(_sniHandler);
            _connection.RemoveMethodHandler(_pathHandler.Path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error destroying tray icon");
        }
    }

    /// <summary>
    /// Sets the tray icon from an SVG file name (without path and extension).
    /// </summary>
    /// <param name="iconName">Icon name, e.g., "trigger-speech-to-text"</param>
    public void SetIcon(string iconName)
    {
        if (_isDisposed)
            return;

        try
        {
            // Check cache first
            if (_iconCache.TryGetValue(iconName, out var cachedIcon))
            {
                _currentIcon = cachedIcon;
                _sniHandler?.SetIcon(_currentIcon);
                _logger.LogDebug("Set icon from cache: {IconName}", iconName);
                return;
            }

            // Load and render SVG
            var iconPath = Path.Combine(_iconsPath, $"{iconName}.svg");
            if (!File.Exists(iconPath))
            {
                _logger.LogWarning("Icon not found: {Path}", iconPath);
                return;
            }

            var pixmap = RenderSvgToArgb(iconPath, _iconSize);
            if (pixmap.HasValue)
            {
                _currentIcon = pixmap.Value;
                _iconCache[iconName] = _currentIcon;
                _sniHandler?.SetIcon(_currentIcon);
                _logger.LogDebug("Set icon: {IconName} ({Width}x{Height})", iconName, _currentIcon.Item1, _currentIcon.Item2);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set icon: {IconName}", iconName);
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
            // Check cache first
            if (_iconCache.TryGetValue(iconName, out var cachedIcon))
            {
                _sniHandler?.SetAttentionIcon(cachedIcon);
                return;
            }

            // Load and render SVG
            var iconPath = Path.Combine(_iconsPath, $"{iconName}.svg");
            if (!File.Exists(iconPath))
            {
                _logger.LogWarning("Attention icon not found: {Path}", iconPath);
                return;
            }

            var pixmap = RenderSvgToArgb(iconPath, _iconSize);
            if (pixmap.HasValue)
            {
                _iconCache[iconName] = pixmap.Value;
                _sniHandler?.SetAttentionIcon(pixmap.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set attention icon: {IconName}", iconName);
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
        _sniHandler?.SetTitleAndTooltip(text);
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
            foreach (var frameName in frameNames)
            {
                if (!_iconCache.ContainsKey(frameName))
                {
                    var iconPath = Path.Combine(_iconsPath, $"{frameName}.svg");
                    if (File.Exists(iconPath))
                    {
                        var pixmap = RenderSvgToArgb(iconPath, _iconSize);
                        if (pixmap.HasValue)
                        {
                            _iconCache[frameName] = pixmap.Value;
                        }
                    }
                }
            }

            _animationFrames = frameNames;
            _currentFrameIndex = 0;

            // Set first frame immediately using animation method with timestamp
            if (_iconCache.TryGetValue(frameNames[0], out var firstFrame))
            {
                _currentIcon = firstFrame;
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

            // Use cached icon if available, call SetAnimationFrame with timestamp to bust cache
            if (_iconCache.TryGetValue(frameName, out var cachedIcon))
            {
                _currentIcon = cachedIcon;
                _sniHandler?.SetAnimationFrame(_currentIcon, _currentFrameIndex);
            }
            else
            {
                // Load icon and set it
                var iconPath = Path.Combine(_iconsPath, $"{frameName}.svg");
                if (File.Exists(iconPath))
                {
                    var pixmap = RenderSvgToArgb(iconPath, _iconSize);
                    if (pixmap.HasValue)
                    {
                        _currentIcon = pixmap.Value;
                        _iconCache[frameName] = _currentIcon;
                        _sniHandler?.SetAnimationFrame(_currentIcon, _currentFrameIndex);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Renders an SVG file to ARGB pixel data for D-Bus.
    /// </summary>
    private (int, int, byte[])? RenderSvgToArgb(string svgPath, int size)
    {
        try
        {
            using var svg = new SKSvg();
            if (svg.Load(svgPath) is null)
            {
                _logger.LogWarning("Failed to load SVG: {Path}", svgPath);
                return null;
            }

            var picture = svg.Picture;
            if (picture is null)
                return null;

            // Calculate scale to fit the target size
            var bounds = picture.CullRect;
            var scale = Math.Min(size / bounds.Width, size / bounds.Height);
            var width = (int)(bounds.Width * scale);
            var height = (int)(bounds.Height * scale);

            if (width <= 0 || height <= 0)
                return null;

            // Create bitmap and render
            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.DrawPicture(picture);

            // Convert RGBA to ARGB for D-Bus (StatusNotifierItem expects ARGB in network byte order)
            var pixels = bitmap.Bytes;
            var argbData = new byte[width * height * 4];

            for (int i = 0; i < width * height; i++)
            {
                var srcIdx = i * 4;
                var dstIdx = i * 4;

                // RGBA -> ARGB (network byte order: ARGB as big-endian)
                byte r = pixels[srcIdx];
                byte g = pixels[srcIdx + 1];
                byte b = pixels[srcIdx + 2];
                byte a = pixels[srcIdx + 3];

                argbData[dstIdx] = a;     // A
                argbData[dstIdx + 1] = r; // R
                argbData[dstIdx + 2] = g; // G
                argbData[dstIdx + 3] = b; // B
            }

            return (width, height, argbData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render SVG: {Path}", svgPath);
            return null;
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
        _serviceWatchDisposable?.Dispose();
        _connection?.Dispose();

        _iconCache.Clear();

        _logger.LogInformation("DBusTrayIcon disposed");
    }
}

/// <summary>
/// D-Bus handler for StatusNotifierItem interface.
/// Implements the server-side of the SNI protocol.
/// </summary>
internal class StatusNotifierItemHandler : OrgKdeStatusNotifierItemHandler
{
    private readonly ILogger _logger;

    public StatusNotifierItemHandler(Connection connection, ILogger logger)
    {
        Connection = connection;
        _logger = logger;

        // Set default values
        Category = "ApplicationStatus";
        Id = "speech-to-text";
        Title = "Speech to Text";
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
        ToolTip = ("", Array.Empty<(int, int, byte[])>(), "", "");
        WindowId = 0;
    }

    public override Connection Connection { get; }

    public event Action? ActivationDelegate;

    protected override ValueTask OnContextMenuAsync(Message message, int x, int y)
    {
        _logger.LogDebug("ContextMenu requested at ({X}, {Y})", x, y);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnActivateAsync(Message message, int x, int y)
    {
        _logger.LogDebug("Activate requested at ({X}, {Y})", x, y);
        ActivationDelegate?.Invoke();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnSecondaryActivateAsync(Message message, int x, int y)
    {
        _logger.LogDebug("SecondaryActivate requested at ({X}, {Y})", x, y);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnScrollAsync(Message message, int delta, string orientation)
    {
        _logger.LogDebug("Scroll: delta={Delta}, orientation={Orientation}", delta, orientation);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sets the icon pixmap. This is the key method that bypasses icon caching.
    /// </summary>
    public void SetIcon((int, int, byte[]) dbusPixmap)
    {
        IconPixmap = new[] { dbusPixmap };
        IconName = ""; // Clear icon name to force pixmap usage
        Status = "Active";

        // Emit signals to notify the tray about the change
        EmitNewIcon();
        EmitNewStatus(Status);
    }

    /// <summary>
    /// Sets the icon pixmap for animation frames.
    /// Changes Id with timestamp to force GNOME Shell to invalidate its cache.
    /// </summary>
    public void SetAnimationFrame((int, int, byte[]) dbusPixmap, int frameIndex)
    {
        // Change Id with timestamp to bust GNOME's icon cache
        Id = $"speech-to-text-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{frameIndex}";
        
        IconPixmap = new[] { dbusPixmap };
        IconName = "";
        Status = "Active";

        // Emit all signals to force refresh
        EmitNewTitle();
        EmitNewIcon();
    }

    /// <summary>
    /// Sets the attention icon pixmap for animation.
    /// Uses NeedsAttention status to force GNOME Shell to refresh the icon.
    /// </summary>
    public void SetAttentionIcon((int, int, byte[]) dbusPixmap)
    {
        AttentionIconPixmap = new[] { dbusPixmap };
        AttentionIconName = "";
        Status = "NeedsAttention";

        // Emit signals - NeedsAttention forces shell to use AttentionIconPixmap
        EmitNewAttentionIcon();
        EmitNewStatus(Status);
    }

    /// <summary>
    /// Sets the title and tooltip text.
    /// </summary>
    public void SetTitleAndTooltip(string text)
    {
        Id = text;
        Title = text;
        Status = "Active";
        ToolTip = ("", Array.Empty<(int, int, byte[])>(), text, "");

        EmitNewTitle();
        EmitNewToolTip();
        EmitNewStatus(Status);
    }
}
