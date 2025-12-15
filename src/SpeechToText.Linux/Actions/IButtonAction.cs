namespace Olbrasoft.SpeechToText.Actions;

/// <summary>
/// Interface for button action handlers (Strategy pattern).
/// Allows different actions to be executed based on click patterns.
/// </summary>
public interface IButtonAction
{
    /// <summary>
    /// Gets the name of this action for logging purposes.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the action asynchronously.
    /// </summary>
    /// <returns>Task representing the async operation.</returns>
    Task ExecuteAsync();
}
