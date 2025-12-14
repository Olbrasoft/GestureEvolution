using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText.TextInput;

/// <summary>
/// Factory for creating the appropriate ITextTyper based on the current display server.
/// Automatically detects X11 vs Wayland and returns the correct implementation.
/// </summary>
public static class TextTyperFactory
{
    /// <summary>
    /// Detects whether the system is running Wayland or X11.
    /// </summary>
    /// <returns>True if running on Wayland, false if X11 or unknown.</returns>
    public static bool IsWayland()
    {
        // Check XDG_SESSION_TYPE first (most reliable)
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (!string.IsNullOrEmpty(sessionType))
        {
            return sessionType.Equals("wayland", StringComparison.OrdinalIgnoreCase);
        }

        // Check WAYLAND_DISPLAY (set when Wayland is active)
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (!string.IsNullOrEmpty(waylandDisplay))
        {
            return true;
        }

        // Fallback: if DISPLAY is set but no WAYLAND_DISPLAY, assume X11
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (!string.IsNullOrEmpty(display))
        {
            return false;
        }

        // Default to Wayland for modern systems (dotool works everywhere)
        return true;
    }

    /// <summary>
    /// Creates the appropriate ITextTyper based on the current display server.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating typed loggers.</param>
    /// <returns>An ITextTyper implementation suitable for the current environment.</returns>
    public static ITextTyper Create(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (IsWayland())
        {
            var dotoolLogger = loggerFactory.CreateLogger<DotoolTextTyper>();
            var dotoolTyper = new DotoolTextTyper(dotoolLogger);
            
            if (dotoolTyper.IsAvailable)
            {
                return dotoolTyper;
            }
            
            // Fallback to xdotool if dotool not available (XWayland apps)
            var xdotoolLogger = loggerFactory.CreateLogger<XdotoolTextTyper>();
            return new XdotoolTextTyper(xdotoolLogger);
        }
        else
        {
            // X11 - prefer xdotool
            var xdotoolLogger = loggerFactory.CreateLogger<XdotoolTextTyper>();
            var xdotoolTyper = new XdotoolTextTyper(xdotoolLogger);
            
            if (xdotoolTyper.IsAvailable)
            {
                return xdotoolTyper;
            }
            
            // Fallback to dotool (works on X11 too via uinput)
            var dotoolLogger = loggerFactory.CreateLogger<DotoolTextTyper>();
            return new DotoolTextTyper(dotoolLogger);
        }
    }

    /// <summary>
    /// Gets the name of the detected display server.
    /// </summary>
    /// <returns>Display server name for logging purposes.</returns>
    public static string GetDisplayServerName()
    {
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (!string.IsNullOrEmpty(sessionType))
        {
            return sessionType;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return "wayland";
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return "x11";
        }

        return "unknown";
    }
}
