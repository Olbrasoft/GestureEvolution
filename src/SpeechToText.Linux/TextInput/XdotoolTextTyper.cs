using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText.TextInput;

/// <summary>
/// Text typer implementation using xdotool for X11 systems.
/// </summary>
public class XdotoolTextTyper : ITextTyper
{
    private readonly ILogger<XdotoolTextTyper> _logger;
    private readonly int _delayBetweenKeystrokes;

    /// <summary>
    /// Initializes a new instance of the <see cref="XdotoolTextTyper"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="delayBetweenKeystrokes">Delay in milliseconds between keystrokes (default: 1ms).</param>
    public XdotoolTextTyper(ILogger<XdotoolTextTyper> logger, int delayBetweenKeystrokes = 1)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _delayBetweenKeystrokes = delayBetweenKeystrokes;
    }

    /// <inheritdoc/>
    public bool IsAvailable
    {
        get
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "xdotool",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                process?.WaitForExit(1000);
                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <inheritdoc/>
    public async Task TypeTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Attempted to type empty or whitespace text");
            return;
        }

        if (!IsAvailable)
        {
            _logger.LogError("xdotool is not available on this system");
            throw new InvalidOperationException("xdotool is not available");
        }

        try
        {
            // Convert to lowercase and add space (as in Python implementation)
            var textToType = text.ToLower() + " ";

            var startInfo = new ProcessStartInfo
            {
                FileName = "xdotool",
                Arguments = $"type --delay {_delayBetweenKeystrokes} \"{textToType}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            process.Start();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                _logger.LogError("xdotool failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                throw new InvalidOperationException($"xdotool failed: {error}");
            }

            _logger.LogDebug("Successfully typed {CharCount} characters", textToType.Length);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Text typing was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to type text: {Text}", text);
            throw;
        }
    }
}
