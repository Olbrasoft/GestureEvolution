namespace Olbrasoft.SpeechToText.Actions;

/// <summary>
/// Null action that does nothing (for single-click when no action is needed).
/// Implements the Null Object pattern.
/// </summary>
public class NoAction : IButtonAction
{
    /// <summary>
    /// Singleton instance of NoAction.
    /// </summary>
    public static readonly NoAction Instance = new();

    private NoAction() { }

    public string Name => "NoAction";

    public Task ExecuteAsync() => Task.CompletedTask;
}
