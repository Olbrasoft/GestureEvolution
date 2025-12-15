namespace Olbrasoft.SpeechToText.TextInput;

/// <summary>
/// Default implementation of IEnvironmentProvider that reads from system environment.
/// </summary>
public class SystemEnvironmentProvider : IEnvironmentProvider
{
    /// <inheritdoc/>
    public string? GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }
}
