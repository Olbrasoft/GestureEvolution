namespace Olbrasoft.SpeechToText.Actions;

/// <summary>
/// Action that simulates a key combination (modifier + key).
/// </summary>
public class KeyComboAction : IButtonAction
{
    private readonly IKeySimulator _keySimulator;
    private readonly KeyCode _modifier;
    private readonly KeyCode _key;

    public KeyComboAction(
        IKeySimulator keySimulator,
        KeyCode modifier,
        KeyCode key,
        string name)
    {
        _keySimulator = keySimulator ?? throw new ArgumentNullException(nameof(keySimulator));
        _modifier = modifier;
        _key = key;
        Name = name;
    }

    public string Name { get; }

    public async Task ExecuteAsync()
    {
        await _keySimulator.SimulateKeyComboAsync(_modifier, _key);
    }
}
