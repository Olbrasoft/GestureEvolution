using NAudio.Wave;
using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText;

/// <summary>
/// Audio recorder implementation using NAudio library.
/// </summary>
public class NAudioRecorder : IAudioRecorder
{
    private readonly ILogger<NAudioRecorder> _logger;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _bitsPerSample;
    private WaveInEvent? _waveIn;
    private readonly List<byte> _recordedData;
    private bool _isRecording;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="NAudioRecorder"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="sampleRate">Sample rate in Hz (default: 16000).</param>
    /// <param name="channels">Number of channels (default: 1 for mono).</param>
    /// <param name="bitsPerSample">Bits per sample (default: 16).</param>
    public NAudioRecorder(
        ILogger<NAudioRecorder> logger,
        int sampleRate = 16000,
        int channels = 1,
        int bitsPerSample = 16)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sampleRate = sampleRate;
        _channels = channels;
        _bitsPerSample = bitsPerSample;
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
            throw new ObjectDisposedException(nameof(NAudioRecorder));

        if (_isRecording)
        {
            _logger.LogWarning("Recording is already in progress");
            return;
        }

        try
        {
            _recordedData.Clear();

            var waveFormat = new WaveFormat(_sampleRate, _bitsPerSample, _channels);
            _waveIn = new WaveInEvent
            {
                WaveFormat = waveFormat,
                BufferMilliseconds = 100
            };

            _waveIn.DataAvailable += OnDataAvailable;

            _waveIn.StartRecording();
            _isRecording = true;

            _logger.LogInformation("Recording started: {SampleRate}Hz, {Channels}ch, {BitsPerSample}bit",
                _sampleRate, _channels, _bitsPerSample);

            // Keep recording until cancellation or StopRecordingAsync is called
            await Task.Run(() =>
            {
                while (_isRecording && !cancellationToken.IsCancellationRequested)
                {
                    Task.Delay(100, cancellationToken).Wait(cancellationToken);
                }
            }, cancellationToken);
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

    /// <inheritdoc/>
    public Task StopRecordingAsync()
    {
        if (!_isRecording)
        {
            _logger.LogWarning("Recording is not active");
            return Task.CompletedTask;
        }

        try
        {
            _isRecording = false;

            if (_waveIn != null)
            {
                _waveIn.StopRecording();
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.Dispose();
                _waveIn = null;
            }

            _logger.LogInformation("Recording stopped. Total data: {ByteCount} bytes", _recordedData.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping recording");
            throw;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public byte[] GetRecordedData()
    {
        return _recordedData.ToArray();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded > 0)
        {
            // Add directly to list using span to avoid intermediate array
            for (int i = 0; i < e.BytesRecorded; i++)
            {
                _recordedData.Add(e.Buffer[i]);
            }

            // Raise event for streaming scenarios (need to copy for event consumers)
            if (AudioDataAvailable is not null)
            {
                var eventData = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, eventData, 0, e.BytesRecorded);
                AudioDataAvailable.Invoke(this, new AudioDataEventArgs(eventData, DateTime.UtcNow));
            }
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

        _waveIn?.Dispose();
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

        _waveIn?.Dispose();
        _recordedData.Clear();

        GC.SuppressFinalize(this);
    }
}
