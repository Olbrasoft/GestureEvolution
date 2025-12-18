using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText;

/// <summary>
/// Discovers input devices in the Linux input subsystem by parsing /proc/bus/input/devices.
/// </summary>
public partial class InputDeviceDiscovery : IInputDeviceDiscovery
{
    private readonly ILogger<InputDeviceDiscovery>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InputDeviceDiscovery"/> class.
    /// </summary>
    /// <param name="logger">Optional logger instance.</param>
    public InputDeviceDiscovery(ILogger<InputDeviceDiscovery>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string? FindDevice(string deviceNamePattern, IEnumerable<string>? excludedDevices = null)
    {
        if (!File.Exists(EvdevConstants.DevicesPath))
        {
            _logger?.LogDebug("Devices file not found: {Path}", EvdevConstants.DevicesPath);
            return null;
        }

        try
        {
            var content = File.ReadAllText(EvdevConstants.DevicesPath);
            var sections = content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            var excludedList = excludedDevices?.ToList() ?? [];

            foreach (var section in sections)
            {
                // Skip excluded devices
                if (excludedList.Any(excluded =>
                    section.Contains(excluded, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Check if this device matches our pattern
                if (!section.Contains(deviceNamePattern, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Find the event handler
                var eventPath = ExtractEventPath(section);
                if (eventPath != null)
                {
                    _logger?.LogDebug("Found device matching '{Pattern}': {Path}", deviceNamePattern, eventPath);
                    return eventPath;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing {Path}", EvdevConstants.DevicesPath);
        }

        return null;
    }

    private static string? ExtractEventPath(string section)
    {
        foreach (var line in section.Split('\n'))
        {
            if (line.StartsWith("H: Handlers="))
            {
                var match = EventRegex().Match(line);
                if (match.Success)
                {
                    var eventPath = $"/dev/input/event{match.Groups[1].Value}";
                    if (File.Exists(eventPath))
                    {
                        return eventPath;
                    }
                }
            }
        }

        return null;
    }

    [GeneratedRegex(@"event(\d+)")]
    private static partial Regex EventRegex();
}
