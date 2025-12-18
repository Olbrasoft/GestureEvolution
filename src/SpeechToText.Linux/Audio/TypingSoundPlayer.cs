using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText.Audio;

/// <summary>
/// Service for playing typing sound during transcription.
/// Uses pw-cat (PipeWire) or paplay (PulseAudio) to play audio.
/// </summary>
public class TypingSoundPlayer : IDisposable
{
    private readonly ILogger<TypingSoundPlayer> _logger;
    private readonly string? _soundFilePath;
    private readonly string? _tearPaperSoundPath;
    private Process? _playProcess;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private readonly object _lock = new();
    private bool _isPlaying;
    private bool _disposed;
    private string? _cachedPlayer;

    /// <summary>
    /// Initializes a new instance with explicit sound file paths.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="soundFilePath">Path to the typing sound file.</param>
    /// <param name="tearPaperSoundPath">Optional path to the tear paper sound file.</param>
    public TypingSoundPlayer(
        ILogger<TypingSoundPlayer> logger,
        string? soundFilePath = null,
        string? tearPaperSoundPath = null)
    {
        _logger = logger;
        _soundFilePath = soundFilePath;
        _tearPaperSoundPath = tearPaperSoundPath;

        ValidateSoundFile(_soundFilePath, "Typing sound");
        ValidateSoundFile(_tearPaperSoundPath, "Tear paper sound");
    }

    /// <summary>
    /// Initializes a new instance using sounds directory relative to application base.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="soundsDirectory">Directory containing sound files.</param>
    /// <param name="typingSoundFileName">Name of the typing sound file (default: write.mp3).</param>
    /// <param name="tearPaperSoundFileName">Name of the tear paper sound file (default: tear-a-paper.mp3).</param>
    public static TypingSoundPlayer CreateFromDirectory(
        ILogger<TypingSoundPlayer> logger,
        string soundsDirectory,
        string typingSoundFileName = "write.mp3",
        string tearPaperSoundFileName = "tear-a-paper.mp3")
    {
        var typingPath = Path.Combine(soundsDirectory, typingSoundFileName);
        var tearPath = Path.Combine(soundsDirectory, tearPaperSoundFileName);

        return new TypingSoundPlayer(logger, typingPath, tearPath);
    }

    private void ValidateSoundFile(string? path, string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogDebug("{Description} disabled (no path configured)", description);
        }
        else if (!File.Exists(path))
        {
            _logger.LogWarning("{Description} file not found: {Path}", description, path);
        }
        else
        {
            _logger.LogInformation("{Description} file: {Path}", description, path);
        }
    }

    /// <summary>
    /// Gets whether sound playback is enabled.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_soundFilePath) && File.Exists(_soundFilePath);

    /// <summary>
    /// Gets whether tear paper sound is available.
    /// </summary>
    public bool HasTearPaperSound => !string.IsNullOrWhiteSpace(_tearPaperSoundPath) && File.Exists(_tearPaperSoundPath);

    /// <summary>
    /// Starts playing the typing sound in a loop.
    /// </summary>
    public void StartLoop()
    {
        lock (_lock)
        {
            if (_isPlaying || _disposed || !IsEnabled)
                return;

            _isPlaying = true;
            _loopCts = new CancellationTokenSource();
            _loopTask = PlayLoopAsync(_loopCts.Token);

            _logger.LogDebug("Typing sound loop started");
        }
    }

    /// <summary>
    /// Stops the typing sound loop.
    /// </summary>
    public void StopLoop()
    {
        lock (_lock)
        {
            if (!_isPlaying)
                return;

            _isPlaying = false;
            _loopCts?.Cancel();

            // Kill any running play process
            try
            {
                if (_playProcess != null && !_playProcess.HasExited)
                {
                    _playProcess.Kill();
                    _playProcess.Dispose();
                    _playProcess = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error stopping play process");
            }

            _logger.LogDebug("Typing sound loop stopped");
        }
    }

    /// <summary>
    /// Plays the tear paper sound once (for rejection feedback).
    /// </summary>
    public async Task PlayTearPaperAsync()
    {
        if (_disposed || !HasTearPaperSound)
            return;

        try
        {
            await PlaySoundOnceAsync(_tearPaperSoundPath!);
            _logger.LogDebug("Tear paper sound played");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to play tear paper sound");
        }
    }

    /// <summary>
    /// Plays a sound file once.
    /// </summary>
    private async Task PlaySoundOnceAsync(string soundPath)
    {
        var player = await GetAvailablePlayerAsync();

        if (string.IsNullOrEmpty(player))
        {
            _logger.LogWarning("No audio player available");
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = player,
            Arguments = GetPlayerArguments(soundPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
        }
    }

    private async Task PlayLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PlayOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error in play loop");
                // Small delay before retry
                try
                {
                    await Task.Delay(100, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task PlayOnceAsync(CancellationToken cancellationToken)
    {
        var player = await GetAvailablePlayerAsync();

        if (string.IsNullOrEmpty(player))
        {
            _logger.LogWarning("No audio player available (tried pw-cat, paplay)");
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = player,
            Arguments = GetPlayerArguments(_soundFilePath!),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        lock (_lock)
        {
            if (!_isPlaying)
                return;

            _playProcess = Process.Start(startInfo);
        }

        if (_playProcess != null)
        {
            try
            {
                await _playProcess.WaitForExitAsync(cancellationToken);
            }
            finally
            {
                lock (_lock)
                {
                    _playProcess?.Dispose();
                    _playProcess = null;
                }
            }
        }
    }

    private async Task<string?> GetAvailablePlayerAsync()
    {
        // Return cached player if already found
        if (_cachedPlayer != null)
            return _cachedPlayer;

        // Check for pw-cat (PipeWire) - requires -p flag for playback mode
        if (await IsCommandAvailableAsync("pw-cat"))
        {
            _cachedPlayer = "pw-cat";
            return _cachedPlayer;
        }

        // Fallback to paplay (PulseAudio) for systems without PipeWire
        if (await IsCommandAvailableAsync("paplay"))
        {
            _cachedPlayer = "paplay";
            return _cachedPlayer;
        }

        return null;
    }

    /// <summary>
    /// Gets the command line arguments for playing a sound file.
    /// </summary>
    private string GetPlayerArguments(string soundPath)
    {
        // pw-cat needs -p flag for playback mode, paplay doesn't
        return _cachedPlayer == "pw-cat"
            ? $"-p \"{soundPath}\""
            : $"\"{soundPath}\"";
    }

    private static async Task<bool> IsCommandAvailableAsync(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopLoop();
        _loopCts?.Dispose();

        GC.SuppressFinalize(this);
    }
}
