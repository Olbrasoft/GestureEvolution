namespace Olbrasoft.SpeechToText;

/// <summary>
/// Interface for discovering input devices in the Linux input subsystem.
/// Abstracts device discovery for testability and single responsibility.
/// </summary>
public interface IInputDeviceDiscovery
{
    /// <summary>
    /// Finds a device matching the specified pattern.
    /// </summary>
    /// <param name="deviceNamePattern">Pattern to match against device name.</param>
    /// <param name="excludedDevices">Optional list of device name patterns to exclude.</param>
    /// <returns>Path to the event device (e.g., /dev/input/event5), or null if not found.</returns>
    string? FindDevice(string deviceNamePattern, IEnumerable<string>? excludedDevices = null);
}
