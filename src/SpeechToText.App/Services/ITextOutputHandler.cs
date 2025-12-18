namespace Olbrasoft.SpeechToText.App.Services;

/// <summary>
/// Handles text output: filtering and typing.
/// Combines text filtering and text typing into a single abstraction.
/// </summary>
public interface ITextOutputHandler
{
    /// <summary>
    /// Processes and outputs the text: applies filters and types the result.
    /// </summary>
    /// <param name="text">The text to process and output.</param>
    /// <returns>The filtered text that was typed, or null if nothing was typed.</returns>
    Task<string?> OutputTextAsync(string text);
}
