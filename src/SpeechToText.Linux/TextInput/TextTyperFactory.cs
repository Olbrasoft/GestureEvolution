using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText.TextInput;

/// <summary>
/// Factory for creating ITextTyper instances.
/// Uses dotool which works on both Wayland and X11 via Linux kernel uinput.
/// </summary>
public class TextTyperFactory : ITextTyperFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IEnvironmentProvider _environment;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextTyperFactory"/> class.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating typed loggers.</param>
    /// <param name="environment">Environment provider for reading environment variables.</param>
    public TextTyperFactory(ILoggerFactory loggerFactory, IEnvironmentProvider environment)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <inheritdoc/>
    public bool IsWayland()
    {
        // Check XDG_SESSION_TYPE first (most reliable)
        var sessionType = _environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (!string.IsNullOrEmpty(sessionType))
        {
            return sessionType.Equals("wayland", StringComparison.OrdinalIgnoreCase);
        }

        // Check WAYLAND_DISPLAY (set when Wayland is active)
        var waylandDisplay = _environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (!string.IsNullOrEmpty(waylandDisplay))
        {
            return true;
        }

        // Fallback: if DISPLAY is set but no WAYLAND_DISPLAY, assume X11
        var display = _environment.GetEnvironmentVariable("DISPLAY");
        if (!string.IsNullOrEmpty(display))
        {
            return false;
        }

        // Default to Wayland for modern systems
        return true;
    }

    /// <inheritdoc/>
    public ITextTyper Create()
    {
        var logger = _loggerFactory.CreateLogger<DotoolTextTyper>();
        var dotoolTyper = new DotoolTextTyper(logger);

        if (dotoolTyper.IsAvailable)
        {
            return dotoolTyper;
        }

        throw new InvalidOperationException(
            "dotool is not installed. Install it from: https://sr.ht/~geb/dotool/");
    }

    /// <inheritdoc/>
    public string GetDisplayServerName()
    {
        var sessionType = _environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (!string.IsNullOrEmpty(sessionType))
        {
            return sessionType;
        }

        if (!string.IsNullOrEmpty(_environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return "wayland";
        }

        if (!string.IsNullOrEmpty(_environment.GetEnvironmentVariable("DISPLAY")))
        {
            return "x11";
        }

        return "unknown";
    }
}
