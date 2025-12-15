namespace Olbrasoft.SpeechToText.TextInput;

/// <summary>
/// Abstraction for environment variable access.
/// Enables unit testing of environment-dependent code.
/// </summary>
public interface IEnvironmentProvider
{
    /// <summary>
    /// Gets the value of an environment variable.
    /// </summary>
    /// <param name="name">The name of the environment variable.</param>
    /// <returns>The value of the environment variable, or null if not set.</returns>
    string? GetEnvironmentVariable(string name);
}
