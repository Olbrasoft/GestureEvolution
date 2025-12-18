using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.Audio;
using Olbrasoft.SpeechToText.Core.Configuration;
using Olbrasoft.SpeechToText.Interop;
using Olbrasoft.SpeechToText.Service.Models;
using Olbrasoft.SpeechToText.Service.Services;

namespace Olbrasoft.SpeechToText.Service.Tray;

/// <summary>
/// System tray indicator for transcription status.
/// Shows animated icon during transcription, hidden otherwise.
/// Replaces the Python transcription-indicator.py script.
/// </summary>
public class TranscriptionTrayService : IDisposable
{
    private readonly ILogger<TranscriptionTrayService> _logger;
    private readonly IPttNotifier _pttNotifier;
    private readonly TypingSoundPlayer _typingSoundPlayer;
    private readonly string _logsViewerUrl;
    private readonly string _version;

    private IntPtr _leftIndicator;   // Left hand gesture indicator
    private IntPtr _centerIndicator; // Robot (main) indicator
    private IntPtr _rightIndicator;  // Right hand gesture indicator

    private string _iconsPath = null!;
    private string[] _frameNames = null!;
    private int _currentFrame;
    private uint _animationTimer;
    private bool _isAnimating;
    private bool _isInitialized;
    private bool _disposed;

    // Animation settings
    private const uint AnimationIntervalMs = 200;
    private const int FrameCount = 5;

    // Keep callbacks alive to prevent GC
    private GObject.GCallback? _aboutCallback;
    private GObject.GCallback? _quitCallback;
    private GLib.GSourceFunc? _animationCallback;
    private GLib.GSourceFunc? _showCallback;
    private GLib.GSourceFunc? _hideCallback;

    private Action? _onQuitRequested;

    public TranscriptionTrayService(
        ILogger<TranscriptionTrayService> logger,
        IPttNotifier pttNotifier,
        TypingSoundPlayer typingSoundPlayer,
        IConfiguration? configuration = null)
    {
        _logger = logger;
        _pttNotifier = pttNotifier;
        _typingSoundPlayer = typingSoundPlayer;

        var endpoints = new ServiceEndpoints();
        configuration?.GetSection(ServiceEndpoints.SectionName).Bind(endpoints);
        _logsViewerUrl = endpoints.LogsViewer;

        _version = typeof(TranscriptionTrayService).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Initializes GTK and creates the tray indicator.
    /// Must be called from the GTK thread.
    /// </summary>
    public void Initialize(Action onQuitRequested)
    {
        _onQuitRequested = onQuitRequested;
        
        // Initialize GTK
        int argc = 0;
        IntPtr argv = IntPtr.Zero;
        Gtk.gtk_init(ref argc, ref argv);
        
        // Setup icon paths
        _iconsPath = Path.Combine(AppContext.BaseDirectory, "icons");

        // Frame names (without extension - AppIndicator adds it)
        _frameNames = new string[FrameCount];
        for (int i = 0; i < FrameCount; i++)
        {
            _frameNames[i] = $"document-white-frame{i + 1}";
        }

        // Create left indicator (left hand gestures)
        _leftIndicator = AppIndicator.app_indicator_new(
            "speech-to-text-left",
            "left-mouse",
            AppIndicator.Category.ApplicationStatus);

        if (_leftIndicator == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create left indicator");
        }

        AppIndicator.app_indicator_set_icon_theme_path(_leftIndicator, _iconsPath);
        AppIndicator.app_indicator_set_title(_leftIndicator, "Left Hand");
        AppIndicator.app_indicator_set_status(_leftIndicator, AppIndicator.Status.Active);

        // Create center indicator with robot icon (always visible)
        _centerIndicator = AppIndicator.app_indicator_new(
            "speech-to-text-center",
            "robot",
            AppIndicator.Category.ApplicationStatus);

        if (_centerIndicator == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create center indicator");
        }

        AppIndicator.app_indicator_set_icon_theme_path(_centerIndicator, _iconsPath);
        AppIndicator.app_indicator_set_title(_centerIndicator, "Speech to Text");
        AppIndicator.app_indicator_set_status(_centerIndicator, AppIndicator.Status.Active);

        // Create right indicator (right hand gestures)
        _rightIndicator = AppIndicator.app_indicator_new(
            "speech-to-text-right",
            "right-mouse",
            AppIndicator.Category.ApplicationStatus);

        if (_rightIndicator == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create right indicator");
        }

        AppIndicator.app_indicator_set_icon_theme_path(_rightIndicator, _iconsPath);
        AppIndicator.app_indicator_set_title(_rightIndicator, "Right Hand");
        AppIndicator.app_indicator_set_status(_rightIndicator, AppIndicator.Status.Active);
        
        // Create menu
        CreateMenu();
        
        // Subscribe to PTT events
        _pttNotifier.PttEventReceived += OnPttEvent;
        
        _isInitialized = true;
        _logger.LogWarning("=== TRAY NOTIFIER HASH: {Hash} ===", _pttNotifier.GetHashCode());
        _logger.LogInformation("TranscriptionTrayService initialized, icons path: {Path}", _iconsPath);
    }

    private void CreateMenu()
    {
        var menu = Gtk.gtk_menu_new();

        // About item
        var aboutItem = Gtk.gtk_menu_item_new_with_label("About");
        Gtk.gtk_menu_shell_append(menu, aboutItem);

        _aboutCallback = (widget, data) => ShowAboutDialog();
        GObject.g_signal_connect_data(aboutItem, "activate", _aboutCallback, IntPtr.Zero, IntPtr.Zero, 0);

        // Separator
        var separator = Gtk.gtk_separator_menu_item_new();
        Gtk.gtk_menu_shell_append(menu, separator);

        // Quit item
        var quitItem = Gtk.gtk_menu_item_new_with_label("Ukončit");
        Gtk.gtk_menu_shell_append(menu, quitItem);

        _quitCallback = (widget, data) =>
        {
            _logger.LogInformation("Quit requested from tray menu");
            _onQuitRequested?.Invoke();
            Gtk.gtk_main_quit();
        };
        GObject.g_signal_connect_data(quitItem, "activate", _quitCallback, IntPtr.Zero, IntPtr.Zero, 0);

        Gtk.gtk_widget_show_all(menu);
        // Menu only on center (robot) indicator
        AppIndicator.app_indicator_set_menu(_centerIndicator, menu);
    }

    private void OnPttEvent(object? sender, PttEvent evt)
    {
        _logger.LogInformation("PttEvent received: {EventType}", evt.EventType);
        
        switch (evt.EventType)
        {
            case PttEventType.RecordingStopped:
                _logger.LogInformation("Recording stopped - showing indicator and starting typing sound");
                ScheduleShow();
                break;
                
            case PttEventType.TranscriptionCompleted:
            case PttEventType.TranscriptionFailed:
                _logger.LogInformation("Transcription finished - hiding indicator and stopping typing sound");
                ScheduleHide();
                break;
        }
    }

    private void ScheduleShow()
    {
        _showCallback = _ =>
        {
            ShowIndicator();
            return false; // Don't repeat
        };
        GLib.g_idle_add(_showCallback, IntPtr.Zero);
    }

    private void ScheduleHide()
    {
        _hideCallback = _ =>
        {
            HideIndicator();
            return false; // Don't repeat
        };
        GLib.g_idle_add(_hideCallback, IntPtr.Zero);
    }

    private void ShowIndicator()
    {
        if (!_isInitialized || _centerIndicator == IntPtr.Zero)
            return;

        AppIndicator.app_indicator_set_status(_centerIndicator, AppIndicator.Status.Active);
        
        // Start typing sound
        _typingSoundPlayer.StartLoop();
        
        // Start animation if not already running
        if (!_isAnimating)
        {
            _currentFrame = 0;
            _animationCallback = AnimateFrame;
            _animationTimer = GLib.g_timeout_add(AnimationIntervalMs, _animationCallback, IntPtr.Zero);
            _isAnimating = true;
            _logger.LogDebug("Animation started");
        }
    }

    private void HideIndicator()
    {
        if (!_isInitialized || _centerIndicator == IntPtr.Zero)
            return;

        AppIndicator.app_indicator_set_status(_centerIndicator, AppIndicator.Status.Passive);
        
        // Stop typing sound
        _typingSoundPlayer.StopLoop();
        
        // Stop animation
        if (_isAnimating && _animationTimer != 0)
        {
            GLib.g_source_remove(_animationTimer);
            _animationTimer = 0;
            _isAnimating = false;
            _logger.LogDebug("Animation stopped");
        }
    }

    private bool AnimateFrame(IntPtr data)
    {
        if (!_isInitialized || _centerIndicator == IntPtr.Zero || !_isAnimating)
            return false;

        _currentFrame = (_currentFrame + 1) % FrameCount;

        // Set icon using full path (AppIndicator needs absolute path for custom icons)
        var iconPath = Path.Combine(_iconsPath, $"{_frameNames[_currentFrame]}.svg");
        AppIndicator.app_indicator_set_icon_full(_centerIndicator, iconPath, "Transcribing...");

        return true; // Continue animation
    }

    private void ShowAboutDialog()
    {
        try
        {
            var aboutText = $"Speech to Text\\n\\n" +
                            $"Version: {_version}\\n\\n" +
                            $"Voice transcription using Whisper AI.\\n" +
                            $"Press configured mouse button to start dictation.\\n\\n" +
                            $"https://github.com/Olbrasoft/SpeechToText";

            var startInfo = new ProcessStartInfo
            {
                FileName = "zenity",
                Arguments = $"--info --title=\"About Speech to Text\" --text=\"{aboutText}\" --width=400",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not show About dialog");
            Console.WriteLine($"Speech to Text v{_version}");
            Console.WriteLine("https://github.com/Olbrasoft/SpeechToText");
        }
    }

    private void OpenLogsInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = _logsViewerUrl,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open logs in browser");
        }
    }

    /// <summary>
    /// Runs the GTK main loop. This blocks until gtk_main_quit is called.
    /// </summary>
    public void RunMainLoop()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("TranscriptionTrayService not initialized");
            
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

    /// <summary>
    /// Updates gesture icons based on detected hand gestures.
    /// </summary>
    /// <param name="isLeftHand">True for left hand, false for right hand</param>
    /// <param name="iconName">Name of icon to display (without .svg extension)</param>
    public void UpdateGestureIcon(bool isLeftHand, string iconName)
    {
        if (!_isInitialized)
            return;

        GLib.g_idle_add(_ =>
        {
            var indicator = isLeftHand ? _leftIndicator : _rightIndicator;
            var iconPath = Path.Combine(_iconsPath, $"{iconName}.svg");

            if (File.Exists(iconPath))
            {
                AppIndicator.app_indicator_set_icon_full(indicator, iconPath,
                    isLeftHand ? "Left Hand Gesture" : "Right Hand Gesture");
                _logger.LogDebug("Updated {Hand} hand icon to {Icon}",
                    isLeftHand ? "left" : "right", iconName);
            }
            else
            {
                _logger.LogWarning("Icon not found: {IconPath}", iconPath);
            }

            return false;
        }, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
            
        _pttNotifier.PttEventReceived -= OnPttEvent;
        
        if (_isAnimating && _animationTimer != 0)
        {
            GLib.g_source_remove(_animationTimer);
        }
        
        _disposed = true;
    }
}
