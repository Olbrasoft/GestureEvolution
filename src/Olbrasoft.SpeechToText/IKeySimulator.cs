namespace Olbrasoft.SpeechToText;

/// <summary>
/// Interface for simulating keyboard input (ISP: separated from keyboard monitoring).
/// </summary>
public interface IKeySimulator
{
    /// <summary>
    /// Simulates a key press and release.
    /// </summary>
    /// <param name="key">The key to simulate.</param>
    Task SimulateKeyPressAsync(KeyCode key);

    /// <summary>
    /// Simulates a key combination (modifier + key).
    /// </summary>
    /// <param name="modifier">The modifier key (e.g., LeftControl).</param>
    /// <param name="key">The key to press with the modifier.</param>
    Task SimulateKeyComboAsync(KeyCode modifier, KeyCode key);

    /// <summary>
    /// Simulates a key combination with two modifiers (modifier1 + modifier2 + key).
    /// </summary>
    /// <param name="modifier1">The first modifier key.</param>
    /// <param name="modifier2">The second modifier key.</param>
    /// <param name="key">The key to press with the modifiers.</param>
    Task SimulateKeyComboAsync(KeyCode modifier1, KeyCode modifier2, KeyCode key);
}
