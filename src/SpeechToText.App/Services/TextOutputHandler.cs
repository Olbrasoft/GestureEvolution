using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.TextInput;

namespace Olbrasoft.SpeechToText.App.Services;

/// <summary>
/// Handles text output: applies filters and types the result.
/// </summary>
public class TextOutputHandler : ITextOutputHandler
{
    private readonly ILogger<TextOutputHandler> _logger;
    private readonly ITextTyper _textTyper;
    private readonly TextFilter? _textFilter;

    public TextOutputHandler(
        ILogger<TextOutputHandler> logger,
        ITextTyper textTyper,
        TextFilter? textFilter = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _textTyper = textTyper ?? throw new ArgumentNullException(nameof(textTyper));
        _textFilter = textFilter;
    }

    /// <inheritdoc />
    public async Task<string?> OutputTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("No text to output");
            return null;
        }

        // Apply text filters
        var filteredText = _textFilter?.Apply(text) ?? text;

        if (string.IsNullOrWhiteSpace(filteredText))
        {
            _logger.LogInformation("Text empty after filtering, nothing to type");
            return null;
        }

        // Type the filtered text
        await _textTyper.TypeTextAsync(filteredText);
        _logger.LogInformation("Text typed successfully: {CharCount} characters", filteredText.Length);

        return filteredText;
    }
}
