using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// Resolves the path to application icons.
/// </summary>
public static class IconsPathResolver
{
    /// <summary>
    /// Finds the icons directory by checking multiple possible locations.
    /// Works for both installed .deb package and development/debug builds.
    /// </summary>
    public static string FindIconsPath(ILogger? logger = null)
    {
        // List of possible icon paths to check (in order of priority)
        var possiblePaths = new[]
        {
            // 1. Installed via .deb package
            "/usr/share/speech-to-text/icons",

            // 2. Same directory as executable (build output)
            Path.Combine(AppContext.BaseDirectory, "icons"),

            // 3. Using assembly location (works better with symlinks)
            Path.Combine(Path.GetDirectoryName(typeof(IconsPathResolver).Assembly.Location) ?? "", "icons"),

            // 4. Development: assets folder relative to source
            Path.Combine(AppContext.BaseDirectory, "..", "..", "assets", "icons"),

            // 5. Development: from project root
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "assets", "icons")
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                // Verify at least one expected icon exists
                var testIcon = Path.Combine(fullPath, "trigger-speech-to-text.svg");
                if (File.Exists(testIcon))
                {
                    logger?.LogInformation("Icons found at: {Path}", fullPath);
                    return fullPath;
                }
            }
        }

        // Fallback to first path even if it doesn't exist (will show warnings later)
        var fallback = Path.Combine(AppContext.BaseDirectory, "icons");
        logger?.LogWarning("Icons directory not found, using fallback: {Path}", fallback);
        return fallback;
    }
}
