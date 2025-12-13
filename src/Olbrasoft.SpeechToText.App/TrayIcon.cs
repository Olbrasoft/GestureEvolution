using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// System tray icon for the SpeechToText desktop application.
/// Provides Start/Stop Dictation and Quit menu items.
/// </summary>
public class TrayIcon : IDisposable
{
    private readonly ILogger<TrayIcon> _logger;
    private readonly DictationService _dictationService;
    private readonly bool _showAnimation;

    private IntPtr _indicator;
    private string _iconsPath = null!;
    private IntPtr _startItem;
    private IntPtr _stopItem;
    private bool _isInitialized;
    private bool _disposed;

    // Icon file names (without path)
    private const string IconIdle = "trigger-speech-to-text";
    private const string IconRecording = "text-to-speech";
    private const string IconOff = "text-to-speech-off";

    // Animation settings
    private const uint AnimationIntervalMs = 200;
    private const int FrameCount = 5;
    private string[] _frameNames = null!;
    private int _currentFrame;
    private uint _animationTimer;
    private bool _isAnimating;
    private GLib.GSourceFunc? _animationCallback;

    // Watchdog timer settings
    private const int WatchdogIntervalMs = 2000;
    private Timer? _watchdogTimer;
    private volatile bool _isIconIdle = true;
    private readonly object _stateLock = new();

    // Keep callbacks alive to prevent GC
    private GObject.GCallback? _startCallback;
    private GObject.GCallback? _stopCallback;
    private GObject.GCallback? _quitCallback;

    public TrayIcon(ILogger<TrayIcon> logger, DictationService dictationService, bool showAnimation = false)
    {
        _logger = logger;
        _dictationService = dictationService;
        _showAnimation = showAnimation;
    }

    /// <summary>
    /// Initializes GTK and creates the tray indicator.
    /// Must be called from the main thread.
    /// </summary>
    public void Initialize()
    {
        // Initialize GTK
        int argc = 0;
        IntPtr argv = IntPtr.Zero;
        Gtk.gtk_init(ref argc, ref argv);

        // Setup icon paths
        _iconsPath = Path.Combine(AppContext.BaseDirectory, "icons");

        if (!Directory.Exists(_iconsPath))
        {
            _logger.LogWarning("Icons directory not found: {Path}, trying assets/icons", _iconsPath);
            _iconsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "assets", "icons");
        }

        _logger.LogInformation("Icons path: {Path}", _iconsPath);

        // Setup animation frame names
        _frameNames = new string[FrameCount];
        for (int i = 0; i < FrameCount; i++)
        {
            _frameNames[i] = $"document-white-frame{i + 1}";
        }

        // Check if animation icons exist
        if (_showAnimation)
        {
            var firstFrame = Path.Combine(_iconsPath, $"{_frameNames[0]}.svg");
            if (!File.Exists(firstFrame))
            {
                _logger.LogWarning("Animation icons not found at {Path}, animation disabled", firstFrame);
            }
            else
            {
                _logger.LogInformation("Animation enabled with {FrameCount} frames", FrameCount);
            }
        }

        // Create app indicator with full path to icon
        var iconPath = Path.Combine(_iconsPath, $"{IconIdle}.svg");
        _logger.LogInformation("Initial icon path: {Path}", iconPath);

        _indicator = AppIndicator.app_indicator_new(
            "speech-to-text",
            iconPath,
            AppIndicator.Category.ApplicationStatus);

        if (_indicator == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create app indicator");
        }
        AppIndicator.app_indicator_set_title(_indicator, "Speech to Text");

        // Start as ACTIVE (visible)
        AppIndicator.app_indicator_set_status(_indicator, AppIndicator.Status.Active);

        // Create menu
        CreateMenu();

        // Subscribe to dictation service events
        _dictationService.StateChanged += OnDictationStateChanged;

        // Start watchdog timer
        _watchdogTimer = new Timer(WatchdogCallback, null, WatchdogIntervalMs, WatchdogIntervalMs);

        _isInitialized = true;
        _logger.LogInformation("TrayIcon initialized with watchdog timer");
    }

    /// <summary>
    /// Watchdog callback - checks icon state consistency every 2 seconds.
    /// If app is idle but icon is not showing idle state, force it to idle.
    /// </summary>
    private void WatchdogCallback(object? state)
    {
        if (!_isInitialized || _disposed)
            return;

        var dictationState = _dictationService.State;

        lock (_stateLock)
        {
            // If dictation is idle but icon is not idle, force icon to idle
            if (dictationState == DictationState.Idle && (!_isIconIdle || _isAnimating))
            {
                _logger.LogWarning("Watchdog: Icon state mismatch detected (DictationState=Idle, IsIconIdle={IsIconIdle}, IsAnimating={IsAnimating}), forcing idle",
                    _isIconIdle, _isAnimating);

                // Schedule icon reset on GTK thread
                GLib.g_idle_add(_ =>
                {
                    ForceIdleIcon();
                    return false;
                }, IntPtr.Zero);
            }
        }
    }

    /// <summary>
    /// Forces the icon to idle state, stopping any animation.
    /// </summary>
    private void ForceIdleIcon()
    {
        if (!_isInitialized || _indicator == IntPtr.Zero)
            return;

        lock (_stateLock)
        {
            // Stop animation if running
            if (_isAnimating && _animationTimer != 0)
            {
                GLib.g_source_remove(_animationTimer);
                _animationTimer = 0;
                _isAnimating = false;
            }

            // Set idle icon
            var iconPath = Path.Combine(_iconsPath, $"{IconIdle}.svg");
            AppIndicator.app_indicator_set_icon_full(_indicator, iconPath, "Idle");
            _isIconIdle = true;

            _logger.LogDebug("Watchdog: Icon forced to idle state");
        }
    }

    private void CreateMenu()
    {
        var menu = Gtk.gtk_menu_new();

        // Start Dictation item
        _startItem = Gtk.gtk_menu_item_new_with_label("▶ Start Dictation");
        Gtk.gtk_menu_shell_append(menu, _startItem);

        _startCallback = (widget, data) =>
        {
            _logger.LogInformation("Start Dictation clicked");
            _ = Task.Run(() => _dictationService.StartDictationAsync());
        };
        GObject.g_signal_connect_data(_startItem, "activate", _startCallback, IntPtr.Zero, IntPtr.Zero, 0);

        // Stop Dictation item (initially disabled)
        _stopItem = Gtk.gtk_menu_item_new_with_label("■ Stop Dictation");
        Gtk.gtk_widget_set_sensitive(_stopItem, false);
        Gtk.gtk_menu_shell_append(menu, _stopItem);

        _stopCallback = (widget, data) =>
        {
            _logger.LogInformation("Stop Dictation clicked");
            _ = Task.Run(() => _dictationService.StopDictationAsync());
        };
        GObject.g_signal_connect_data(_stopItem, "activate", _stopCallback, IntPtr.Zero, IntPtr.Zero, 0);

        // Separator
        var separator = Gtk.gtk_separator_menu_item_new();
        Gtk.gtk_menu_shell_append(menu, separator);

        // Quit item
        var quitItem = Gtk.gtk_menu_item_new_with_label("✕ Quit");
        Gtk.gtk_menu_shell_append(menu, quitItem);

        _quitCallback = (widget, data) =>
        {
            _logger.LogInformation("Quit requested from tray menu");
            Gtk.gtk_main_quit();
        };
        GObject.g_signal_connect_data(quitItem, "activate", _quitCallback, IntPtr.Zero, IntPtr.Zero, 0);

        Gtk.gtk_widget_show_all(menu);
        AppIndicator.app_indicator_set_menu(_indicator, menu);
    }

    private void OnDictationStateChanged(object? sender, DictationState state)
    {
        // Schedule UI update on GTK thread
        GLib.g_idle_add(_ =>
        {
            UpdateIcon(state);
            UpdateMenuItems(state);
            return false; // Don't repeat
        }, IntPtr.Zero);
    }

    private void UpdateIcon(DictationState state)
    {
        if (!_isInitialized || _indicator == IntPtr.Zero)
            return;

        lock (_stateLock)
        {
            // Handle animation for Transcribing state
            if (state == DictationState.Transcribing && _showAnimation)
            {
                StartAnimationLocked();
                return;
            }

            // Always stop animation attempt (safe even if not animating - fixes race condition)
            StopAnimationLocked();

            // Always set icon based on state (this is the key fix - icon is always set here)
            // Recording = black icon, Transcribing/Idle = white icon
            var iconPath = state switch
            {
                DictationState.Recording => Path.Combine(_iconsPath, $"{IconRecording}.svg"),
                _ => Path.Combine(_iconsPath, $"{IconIdle}.svg")
            };

            AppIndicator.app_indicator_set_icon_full(_indicator, iconPath, state.ToString());
            _isIconIdle = (state == DictationState.Idle);
        }
    }

    private void StartAnimationLocked()
    {
        // Must be called with _stateLock held
        if (_isAnimating)
            return;

        var firstFrame = Path.Combine(_iconsPath, $"{_frameNames[0]}.svg");
        if (!File.Exists(firstFrame))
        {
            _logger.LogWarning("Animation icons not found, using static icon");
            var iconPath = Path.Combine(_iconsPath, $"{IconRecording}.svg");
            AppIndicator.app_indicator_set_icon_full(_indicator, iconPath, "Transcribing");
            _isIconIdle = false;
            return;
        }

        _currentFrame = 0;
        _animationCallback = AnimateFrame;
        _animationTimer = GLib.g_timeout_add(AnimationIntervalMs, _animationCallback, IntPtr.Zero);
        _isAnimating = true;
        _isIconIdle = false;
        _logger.LogDebug("Tray icon animation started");
    }

    private void StopAnimationLocked()
    {
        // Must be called with _stateLock held
        // Always try to stop timer, even if _isAnimating is false (race condition safety)
        if (_animationTimer != 0)
        {
            GLib.g_source_remove(_animationTimer);
            _animationTimer = 0;
        }

        _isAnimating = false;
        // Note: Icon is set by UpdateIcon after calling this method
        _logger.LogDebug("Tray icon animation stopped");
    }

    private bool AnimateFrame(IntPtr data)
    {
        if (!_isInitialized || _indicator == IntPtr.Zero || !_isAnimating)
            return false;

        _currentFrame = (_currentFrame + 1) % FrameCount;

        var iconPath = Path.Combine(_iconsPath, $"{_frameNames[_currentFrame]}.svg");
        AppIndicator.app_indicator_set_icon_full(_indicator, iconPath, "Transcribing...");

        return true; // Continue animation
    }

    private void UpdateMenuItems(DictationState state)
    {
        if (!_isInitialized)
            return;

        bool isRecording = state == DictationState.Recording || state == DictationState.Transcribing;

        Gtk.gtk_widget_set_sensitive(_startItem, !isRecording);
        Gtk.gtk_widget_set_sensitive(_stopItem, isRecording);
    }

    /// <summary>
    /// Runs the GTK main loop. This blocks until gtk_main_quit is called.
    /// </summary>
    public void RunMainLoop()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("TrayIcon not initialized");

        _logger.LogInformation("Starting GTK main loop");
        Gtk.gtk_main();
    }

    /// <summary>
    /// Quits the GTK main loop from another thread.
    /// </summary>
    public void QuitMainLoop()
    {
        if (_isInitialized)
        {
            GLib.g_idle_add(_ =>
            {
                Gtk.gtk_main_quit();
                return false;
            }, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _dictationService.StateChanged -= OnDictationStateChanged;

        // Stop watchdog timer
        _watchdogTimer?.Dispose();
        _watchdogTimer = null;

        // Stop animation if running
        lock (_stateLock)
        {
            if (_isAnimating && _animationTimer != 0)
            {
                GLib.g_source_remove(_animationTimer);
            }
        }

        _disposed = true;
    }
}
