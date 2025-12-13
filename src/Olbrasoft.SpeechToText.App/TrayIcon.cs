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
    private const string IconRecording = "trigger-speech-to-text-recording"; // Orange icon for recording
    private const string IconOff = "text-to-speech-off";

    // Animation settings (CancellationToken-based for reliable cancellation)
    private const int AnimationIntervalMs = 200;
    private const int FrameCount = 5;
    private string[] _frameNames = null!;
    private int _currentFrame;
    private CancellationTokenSource? _animationCts;
    private volatile bool _isAnimating;
    private long _animationGeneration; // Prevents stale icon updates

    // Watchdog timer settings (uses GTK timer for reliable main-thread execution)
    private const uint WatchdogIntervalMs = 2000;
    private uint _watchdogGtkTimer;
    private GLib.GSourceFunc? _watchdogCallback;
    private volatile bool _isIconIdle = true;
    private readonly object _stateLock = new();

    // Keep callbacks alive to prevent GC
    private GObject.GCallback? _startCallback;
    private GObject.GCallback? _stopCallback;
    private GObject.GCallback? _aboutCallback;
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

        // Create app indicator with icon NAME only (not full path)
        // Using icon theme path + icon names fixes GNOME Shell icon caching issues
        _logger.LogInformation("Setting icon theme path: {Path}", _iconsPath);

        _indicator = AppIndicator.app_indicator_new(
            "speech-to-text",
            IconIdle,  // Use icon name only, not full path
            AppIndicator.Category.ApplicationStatus);
        
        // Set icon theme path AFTER creating indicator - this tells AppIndicator where to find icons
        AppIndicator.app_indicator_set_icon_theme_path(_indicator, _iconsPath);

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

        // Start GTK-native watchdog timer (runs on GTK main thread - reliable)
        _watchdogCallback = WatchdogGtkCallback;
        _watchdogGtkTimer = GLib.g_timeout_add(WatchdogIntervalMs, _watchdogCallback, IntPtr.Zero);

        _isInitialized = true;
        _logger.LogInformation("TrayIcon initialized with GTK watchdog timer");
    }

    /// <summary>
    /// GTK watchdog callback - runs on GTK main thread every 2 seconds.
    /// Forces idle icon when app is idle to fix stuck animation frames.
    /// </summary>
    private bool WatchdogGtkCallback(IntPtr data)
    {
        if (_disposed)
            return false; // Stop timer

        if (!_isInitialized || _indicator == IntPtr.Zero)
            return true; // Keep timer, but skip this tick

        var dictationState = _dictationService.State;

        // Force idle icon when app is idle - catches race conditions
        if (dictationState == DictationState.Idle && !_isIconIdle)
        {
            _logger.LogWarning("Watchdog: Icon out of sync, forcing to idle");
            ForceIdleIcon(); // Already on GTK thread, no g_idle_add needed
        }

        return true; // Keep timer running
    }

    /// <summary>
    /// Forces the icon to idle state, stopping any animation.
    /// Called by watchdog when icon is out of sync.
    /// </summary>
    private void ForceIdleIcon()
    {
        if (!_isInitialized || _indicator == IntPtr.Zero)
            return;

        lock (_stateLock)
        {
            // Cancel animation if running
            if (_isAnimating)
            {
                _animationCts?.Cancel();
                _animationCts = null;
                _animationGeneration++;
                _isAnimating = false;
            }

            // Set idle icon (use icon name only, not full path)
            AppIndicator.app_indicator_set_icon_full(_indicator, IconIdle, "Idle");
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

        // About item
        var aboutItem = Gtk.gtk_menu_item_new_with_label("ℹ About");
        Gtk.gtk_menu_shell_append(menu, aboutItem);

        _aboutCallback = (widget, data) =>
        {
            _logger.LogInformation("About dialog requested");
            ShowAboutDialog();
        };
        GObject.g_signal_connect_data(aboutItem, "activate", _aboutCallback, IntPtr.Zero, IntPtr.Zero, 0);

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
            // Recording = orange icon, Transcribing/Idle = white icon
            // Use icon NAMES only (not full paths) - icon theme path is set during initialization
            var iconName = state switch
            {
                DictationState.Recording => IconRecording,
                _ => IconIdle
            };

            AppIndicator.app_indicator_set_icon_full(_indicator, iconName, state.ToString());
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
            AppIndicator.app_indicator_set_icon_full(_indicator, IconRecording, "Transcribing");
            _isIconIdle = false;
            return;
        }

        // Cancel any previous animation
        _animationCts?.Cancel();
        _animationCts = new CancellationTokenSource();

        _currentFrame = 0;
        _isAnimating = true;
        _isIconIdle = false;
        _animationGeneration++;

        var generation = _animationGeneration;
        var cts = _animationCts;

        // Start animation loop on background thread
        _ = RunAnimationLoopAsync(generation, cts.Token);

        _logger.LogDebug("Tray icon animation started (generation {Generation})", generation);
    }

    private void StopAnimationLocked()
    {
        // Must be called with _stateLock held
        // Cancel the animation task - this is clean and reliable
        _animationCts?.Cancel();
        _animationCts = null;

        // Increment generation to invalidate any pending icon updates
        _animationGeneration++;

        // Set flag to false
        _isAnimating = false;

        // NOTE: Do NOT set icon here - the caller (UpdateIcon) sets the correct icon
        // based on actual state. Setting idle icon here via g_idle_add caused race
        // conditions where the Recording icon was immediately overridden by idle.
        _logger.LogDebug("Animation stopped (generation {Generation})", _animationGeneration);
    }

    /// <summary>
    /// Async animation loop using CancellationToken for reliable cancellation.
    /// Runs on background thread, marshals icon updates to GTK thread.
    /// </summary>
    private async Task RunAnimationLoopAsync(long generation, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = _currentFrame;
                var iconName = _frameNames[frame];  // Just icon name, no path

                // Marshal icon update to GTK thread
                GLib.g_idle_add(_ =>
                {
                    // Only update if this animation is still current
                    if (_animationGeneration == generation && _isAnimating && !ct.IsCancellationRequested)
                    {
                        AppIndicator.app_indicator_set_icon_full(_indicator, iconName, "Transcribing...");
                    }
                    return false;
                }, IntPtr.Zero);

                _currentFrame = (frame + 1) % FrameCount;

                // Wait with cancellation support
                await Task.Delay(AnimationIntervalMs, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when animation is cancelled - clean exit
            _logger.LogDebug("Animation loop cancelled (generation {Generation})", generation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Animation loop error");
        }
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
    /// Shows the About dialog with application information.
    /// </summary>
    private void ShowAboutDialog()
    {
        var dialog = Gtk.gtk_about_dialog_new();

        if (dialog == IntPtr.Zero)
        {
            _logger.LogError("Failed to create About dialog");
            return;
        }

        try
        {
            // Get version from assembly
            var version = typeof(Program).Assembly.GetName().Version;
            var versionString = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";

            Gtk.gtk_about_dialog_set_program_name(dialog, "Speech to Text");
            Gtk.gtk_about_dialog_set_version(dialog, versionString);
            Gtk.gtk_about_dialog_set_comments(dialog,
                "Voice dictation application for Linux.\n" +
                "Press CapsLock to start/stop dictation.\n" +
                "Transcribed text is typed automatically.");
            Gtk.gtk_about_dialog_set_website(dialog, "https://github.com/Olbrasoft/SpeechToText");
            Gtk.gtk_about_dialog_set_website_label(dialog, "GitHub Repository");

            // Try to use the app icon (logo_icon_name expects icon name, not path)
            // Note: For About dialog, we still need to check if icon file exists
            var iconPath = Path.Combine(_iconsPath, $"{IconIdle}.svg");
            if (File.Exists(iconPath))
            {
                // Set icon theme path for About dialog too, then use icon name
                Gtk.gtk_about_dialog_set_logo_icon_name(dialog, IconIdle);
            }

            _logger.LogDebug("Showing About dialog, version: {Version}", versionString);

            // Run the dialog (blocks until closed)
            Gtk.gtk_dialog_run(dialog);
        }
        finally
        {
            // Destroy the dialog
            Gtk.gtk_widget_destroy(dialog);
        }
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

        // Stop GTK watchdog timer
        if (_watchdogGtkTimer != 0)
        {
            GLib.g_source_remove(_watchdogGtkTimer);
            _watchdogGtkTimer = 0;
        }

        // Stop animation if running
        lock (_stateLock)
        {
            _animationCts?.Cancel();
            _animationCts?.Dispose();
            _animationCts = null;
            _isAnimating = false;
        }

        _disposed = true;
    }
}
