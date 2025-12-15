namespace Olbrasoft.SpeechToText.Actions;

/// <summary>
/// Action that simulates a single key press.
/// </summary>
public class KeyPressAction : IButtonAction
{
    private readonly IKeySimulator _keySimulator;
    private readonly IKeyboardMonitor _keyboardMonitor;
    private readonly KeyCode _keyCode;
    private readonly bool _raiseReleaseEvent;

    public KeyPressAction(
        IKeySimulator keySimulator,
        IKeyboardMonitor keyboardMonitor,
        KeyCode keyCode,
        string name,
        bool raiseReleaseEvent = true)
    {
        _keySimulator = keySimulator ?? throw new ArgumentNullException(nameof(keySimulator));
        _keyboardMonitor = keyboardMonitor ?? throw new ArgumentNullException(nameof(keyboardMonitor));
        _keyCode = keyCode;
        Name = name;
        _raiseReleaseEvent = raiseReleaseEvent;
    }

    public string Name { get; }

    public async Task ExecuteAsync()
    {
        await _keySimulator.SimulateKeyPressAsync(_keyCode);
        if (_raiseReleaseEvent)
        {
            await Task.Delay(MultiClickDetector.KeySimulationDelayMs);
            _keyboardMonitor.RaiseKeyReleasedEvent(_keyCode);
        }
    }
}
