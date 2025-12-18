using System.Buffers;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText.Audio;

/// <summary>
/// Linux audio recorder using pw-cat (PipeWire) or arecord (ALSA) command-line tool.
/// </summary>
public class PipeWireAudioRecorder : IAudioRecorder
{
    private readonly ILogger<PipeWireAudioRecorder> _logger;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _bitsPerSample;
    private readonly string _deviceName;
    private Process? _recordProcess;
    private readonly List<byte> _recordedData;
    private bool _isRecording;
    private bool _disposed;
    private Task? _captureTask;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipeWireAudioRecorder"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="sampleRate">Sample rate in Hz (default: 16000).</param>
    /// <param name="channels">Number of channels (default: 1 for mono).</param>
    /// <param name="bitsPerSample">Bits per sample (default: 16).</param>
    /// <param name="deviceName">ALSA device name (optional, uses default if not specified).</param>
    public PipeWireAudioRecorder(
        ILogger<PipeWireAudioRecorder> logger,
        int sampleRate = 16000,
        int channels = 1,
        int bitsPerSample = 16,
        string? deviceName = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sampleRate = sampleRate;
        _channels = channels;
        _bitsPerSample = bitsPerSample;
        _deviceName = deviceName ?? "default";
        _recordedData = new List<byte>();
    }

    /// <inheritdoc/>
    public event EventHandler<AudioDataEventArgs>? AudioDataAvailable;

    /// <inheritdoc/>
    public int SampleRate => _sampleRate;

    /// <inheritdoc/>
    public int Channels => _channels;

    /// <inheritdoc/>
    public int BitsPerSample => _bitsPerSample;

    /// <inheritdoc/>
    public bool IsRecording => _isRecording;

    /// <inheritdoc/>
    public async Task StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PipeWireAudioRecorder));

        if (_isRecording)
        {
            _logger.LogWarning("Recording is already in progress");
            return;
        }

        try
        {
            _recordedData.Clear();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Try pw-cat first (PipeWire), fallback to arecord (ALSA)
            var usePipeWire = CheckCommandAvailable("pw-cat");
            var recordCommand = usePipeWire ? "pw-cat" : "arecord";

            var arguments = usePipeWire
                ? $"-r --format s16 --rate {_sampleRate} --channels {_channels} -"
                : $"-f S16_LE -r {_sampleRate} -c {_channels} -D {_deviceName} -";

            _recordProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = recordCommand,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _recordProcess.ErrorDataReceived += (sender, e) =>
            {
                // Suppress stderr output
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogDebug("{RecordCommand} stderr: {Error}", recordCommand, e.Data);
                }
            };

            _recordProcess.Start();
            _recordProcess.BeginErrorReadLine();

            _isRecording = true;
            _logger.LogInformation("Recording started using {Command}: {SampleRate}Hz, {Channels}ch, {BitsPerSample}bit",
                recordCommand, _sampleRate, _channels, _bitsPerSample);

            // Start reading audio data in background task
            _captureTask = Task.Run(async () => await CaptureAudioLoop(_cts.Token), _cts.Token);

            // Wait for completion or cancellation
            await _captureTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Recording was cancelled");
            await StopRecordingAsync();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recording");
            _isRecording = false;
            throw;
        }
    }

    private async Task CaptureAudioLoop(CancellationToken cancellationToken)
    {
        const int frameSizeInSamples = 1024; // Audio chunk size
        const int bytesPerSample = 2;        // 16-bit = 2 bytes
        int frameSize = frameSizeInSamples * bytesPerSample * _channels;

        // Rent buffer from pool instead of allocating
        byte[] buffer = ArrayPool<byte>.Shared.Rent(frameSize);

        try
        {
            var stream = _recordProcess!.StandardOutput.BaseStream;

            while (_isRecording && !cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, frameSize), cancellationToken);

                if (bytesRead == 0)
                {
                    break; // End of stream
                }

                // Add directly to list using span to avoid intermediate array
                for (int i = 0; i < bytesRead; i++)
                {
                    _recordedData.Add(buffer[i]);
                }

                // Raise event for streaming scenarios (need to copy for event consumers)
                if (AudioDataAvailable is not null)
                {
                    var eventData = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, eventData, 0, bytesRead);
                    AudioDataAvailable.Invoke(this, new AudioDataEventArgs(eventData, DateTime.UtcNow));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio capture error");
        }
        finally
        {
            // Always return buffer to pool
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc/>
    public async Task StopRecordingAsync()
    {
        if (!_isRecording)
        {
            _logger.LogWarning("Recording is not active");
            return;
        }

        try
        {
            _isRecording = false;
            _cts?.Cancel();

            // Kill record process
            if (_recordProcess != null && !_recordProcess.HasExited)
            {
                _recordProcess.Kill(entireProcessTree: true);
                await _recordProcess.WaitForExitAsync();
            }

            // Wait for capture task to complete
            if (_captureTask != null)
            {
                await _captureTask;
            }

            _logger.LogInformation("Recording stopped. Total data: {ByteCount} bytes", _recordedData.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping recording");
            throw;
        }
    }

    /// <inheritdoc/>
    public byte[] GetRecordedData()
    {
        return _recordedData.ToArray();
    }

    private static bool CheckCommandAvailable(string command)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_isRecording)
        {
            StopRecordingAsync().GetAwaiter().GetResult();
        }

        _cts?.Dispose();
        _recordProcess?.Dispose();
        _recordedData.Clear();

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_isRecording)
        {
            await StopRecordingAsync();
        }

        _cts?.Dispose();
        _recordProcess?.Dispose();
        _recordedData.Clear();

        GC.SuppressFinalize(this);
    }
}
