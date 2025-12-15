namespace Olbrasoft.SpeechToText.Actions;

/// <summary>
/// Action that simulates a key combination with two modifiers (modifier1 + modifier2 + key).
/// </summary>
public class KeyComboWithTwoModifiersAction : IButtonAction
{
    private readonly IKeySimulator _keySimulator;
    private readonly KeyCode _modifier1;
    private readonly KeyCode _modifier2;
    private readonly KeyCode _key;

    public KeyComboWithTwoModifiersAction(
        IKeySimulator keySimulator,
        KeyCode modifier1,
        KeyCode modifier2,
        KeyCode key,
        string name)
    {
        _keySimulator = keySimulator ?? throw new ArgumentNullException(nameof(keySimulator));
        _modifier1 = modifier1;
        _modifier2 = modifier2;
        _key = key;
        Name = name;
    }

    public string Name { get; }

    public async Task ExecuteAsync()
    {
        await _keySimulator.SimulateKeyComboAsync(_modifier1, _modifier2, _key);
    }
}
