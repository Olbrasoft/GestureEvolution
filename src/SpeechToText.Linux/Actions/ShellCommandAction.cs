using System.Diagnostics;

namespace Olbrasoft.SpeechToText.Actions;

/// <summary>
/// Action that executes a shell command.
/// </summary>
public class ShellCommandAction : IButtonAction
{
    private readonly string _command;

    public ShellCommandAction(string command, string name)
    {
        _command = command ?? throw new ArgumentNullException(nameof(command));
        Name = name;
    }

    public string Name { get; }

    public Task ExecuteAsync()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{_command}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        return Task.CompletedTask;
    }
}
