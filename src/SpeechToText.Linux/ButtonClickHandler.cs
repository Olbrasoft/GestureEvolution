using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText;

/// <summary>
/// Handles button click patterns and executes corresponding actions.
/// Combines MultiClickDetector with action strategy pattern.
/// </summary>
public class ButtonClickHandler : IDisposable
{
    private readonly ILogger? _logger;
    private readonly string _buttonName;
    private readonly MultiClickDetector _clickDetector;
    private readonly IButtonAction _singleClickAction;
    private readonly IButtonAction _doubleClickAction;
    private readonly IButtonAction _tripleClickAction;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ButtonClickHandler"/> class.
    /// </summary>
    /// <param name="buttonName">Name of the button (for logging).</param>
    /// <param name="singleClickAction">Action for single click.</param>
    /// <param name="doubleClickAction">Action for double click.</param>
    /// <param name="tripleClickAction">Action for triple click.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="maxClickCount">Maximum clicks to detect (2 or 3).</param>
    public ButtonClickHandler(
        string buttonName,
        IButtonAction singleClickAction,
        IButtonAction doubleClickAction,
        IButtonAction tripleClickAction,
        ILogger? logger = null,
        int maxClickCount = 3)
    {
        _buttonName = buttonName;
        _singleClickAction = singleClickAction ?? throw new ArgumentNullException(nameof(singleClickAction));
        _doubleClickAction = doubleClickAction ?? throw new ArgumentNullException(nameof(doubleClickAction));
        _tripleClickAction = tripleClickAction ?? throw new ArgumentNullException(nameof(tripleClickAction));
        _logger = logger;

        _clickDetector = new MultiClickDetector(buttonName, null, maxClickCount);
        _clickDetector.ClickDetected += OnClickDetected;
    }

    /// <summary>
    /// Registers a button click.
    /// </summary>
    public void RegisterClick()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ButtonClickHandler));

        _clickDetector.RegisterClick();
    }

    /// <summary>
    /// Resets the click detector state.
    /// </summary>
    public void Reset()
    {
        _clickDetector.Reset();
    }

    private async void OnClickDetected(object? sender, ClickDetectedEventArgs e)
    {
        var action = e.Result switch
        {
            ClickResult.SingleClick => _singleClickAction,
            ClickResult.DoubleClick => _doubleClickAction,
            ClickResult.TripleClick => _tripleClickAction,
            _ => NoAction.Instance
        };

        if (action == NoAction.Instance)
        {
            _logger?.LogDebug("{Button} {Result} - no action configured", _buttonName, e.Result);
            return;
        }

        _logger?.LogInformation("{Button} {Result} - executing {Action}", _buttonName, e.Result, action.Name);

        try
        {
            await action.ExecuteAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to execute {Action} on {Button} {Result}",
                action.Name, _buttonName, e.Result);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _clickDetector.ClickDetected -= OnClickDetected;
        _clickDetector.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
