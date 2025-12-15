using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText.TextInput;

/// <summary>
/// Factory for creating the appropriate ITextTyper based on the current display server.
/// Automatically detects X11 vs Wayland and returns the correct implementation.
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

        // Default to Wayland for modern systems (dotool works everywhere)
        return true;
    }

    /// <inheritdoc/>
    public ITextTyper Create()
    {
        if (IsWayland())
        {
            var dotoolLogger = _loggerFactory.CreateLogger<DotoolTextTyper>();
            var dotoolTyper = new DotoolTextTyper(dotoolLogger);

            if (dotoolTyper.IsAvailable)
            {
                return dotoolTyper;
            }

            // Fallback to xdotool if dotool not available (XWayland apps)
            var xdotoolLogger = _loggerFactory.CreateLogger<XdotoolTextTyper>();
            return new XdotoolTextTyper(xdotoolLogger);
        }
        else
        {
            // X11 - prefer xdotool
            var xdotoolLogger = _loggerFactory.CreateLogger<XdotoolTextTyper>();
            var xdotoolTyper = new XdotoolTextTyper(xdotoolLogger);

            if (xdotoolTyper.IsAvailable)
            {
                return xdotoolTyper;
            }

            // Fallback to dotool (works on X11 too via uinput)
            var dotoolLogger = _loggerFactory.CreateLogger<DotoolTextTyper>();
            return new DotoolTextTyper(dotoolLogger);
        }
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
