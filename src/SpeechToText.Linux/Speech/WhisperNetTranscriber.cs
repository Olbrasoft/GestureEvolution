using Microsoft.Extensions.Logging;
using Whisper.net;

namespace Olbrasoft.SpeechToText.Speech;

/// <summary>
/// Whisper.net based speech transcriber with CUDA GPU support.
/// Uses whisper.cpp native bindings for high-quality transcription.
/// </summary>
public class WhisperNetTranscriber : ISpeechTranscriber
{
    private readonly ILogger<WhisperNetTranscriber> _logger;
    private readonly string _modelPath;
    private readonly string _language;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _transcribeLock = new(1, 1);
    private WhisperFactory? _whisperFactory;
    private WhisperProcessor? _processor;
    private bool _disposed;

    // Whisper audio configuration
    private const int SampleRate = 16000;

    /// <summary>
    /// Initializes a new instance of the <see cref="WhisperNetTranscriber"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="modelPath">Path to ggml model file (e.g., ggml-medium.bin).</param>
    /// <param name="language">Language code (e.g., "cs" for Czech, "en" for English, "auto" for auto-detect).</param>
    public WhisperNetTranscriber(ILogger<WhisperNetTranscriber> logger, string modelPath, string language = "cs")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        _language = language ?? throw new ArgumentNullException(nameof(language));

        if (!File.Exists(_modelPath))
        {
            throw new FileNotFoundException($"Whisper model file not found: {_modelPath}");
        }
    }

    /// <inheritdoc/>
    public string Language => _language;

    /// <summary>
    /// Initializes the Whisper.net factory and processor (lazy initialization).
    /// Thread-safe via semaphore.
    /// </summary>
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Quick check without lock
        if (_whisperFactory != null && _processor != null)
            return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_whisperFactory != null && _processor != null)
                return;

            _logger.LogInformation("Loading Whisper.net model from: {ModelPath}", _modelPath);

            // Create factory - Whisper.net automatically selects best runtime (CUDA > CPU)
            _whisperFactory = WhisperFactory.FromPath(_modelPath);

            // Create processor with configuration optimized for Czech speech recognition
            _processor = _whisperFactory.CreateBuilder()
                .WithLanguage(_language)
                // Use beam search for better accuracy (default beam size is 5)
                .WithBeamSearchSamplingStrategy()
                .ParentBuilder
                // Lower temperature = more deterministic output
                .WithTemperature(0.0f)
                // CRITICAL: Disable context between transcriptions to prevent hallucinations
                // Without this, Whisper uses previous transcription as prompt for next one,
                // causing it to "hallucinate" and repeat previous text in new transcriptions
                .WithNoContext()
                .Build();

            _logger.LogInformation("Whisper.net initialized successfully, language: {Language}", _language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Whisper.net");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Converts PCM byte array to float32 samples normalized to [-1.0, 1.0].
    /// </summary>
    private static float[] ConvertPcmToFloat32(byte[] pcmData)
    {
        var samples = new float[pcmData.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short sample = BitConverter.ToInt16(pcmData, i * 2);
            samples[i] = sample / 32768.0f;
        }
        return samples;
    }

    /// <summary>
    /// Strips WAV header if present (first 44 bytes).
    /// </summary>
    private static byte[] StripWavHeader(byte[] audioData)
    {
        if (audioData.Length > 44 &&
            audioData[0] == 'R' && audioData[1] == 'I' &&
            audioData[2] == 'F' && audioData[3] == 'F')
        {
            var pcmData = new byte[audioData.Length - 44];
            Array.Copy(audioData, 44, pcmData, 0, pcmData.Length);
            return pcmData;
        }
        return audioData;
    }

    /// <inheritdoc/>
    public async Task<TranscriptionResult> TranscribeAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WhisperNetTranscriber));

        // Ensure only one transcription runs at a time - Whisper.net is NOT thread-safe
        await _transcribeLock.WaitAsync(cancellationToken);
        try
        {
            return await TranscribeInternalAsync(audioData, cancellationToken);
        }
        finally
        {
            _transcribeLock.Release();
        }
    }

    private async Task<TranscriptionResult> TranscribeInternalAsync(byte[] audioData, CancellationToken cancellationToken)
    {
        try
        {
            await InitializeAsync(cancellationToken);

            _logger.LogDebug("Starting transcription... (audio size: {Size} bytes)", audioData.Length);

            // Strip WAV header if present
            var pcmData = StripWavHeader(audioData);

            // Convert PCM to float32 samples
            var samples = ConvertPcmToFloat32(pcmData);
            
            _logger.LogDebug("Audio samples: {Count} ({Seconds:F1}s)", 
                samples.Length, samples.Length / (float)SampleRate);

            // Process with Whisper.net
            var segments = new List<string>();
            
            _logger.LogDebug("Starting Whisper processing...");
            try
            {
                await foreach (var segment in _processor!.ProcessAsync(samples, cancellationToken))
                {
                    // Check for cancellation between segments
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!string.IsNullOrWhiteSpace(segment.Text))
                    {
                        segments.Add(segment.Text.Trim());
                        _logger.LogDebug("Segment: {Start} -> {End}: {Text}",
                            segment.Start, segment.End, segment.Text);
                    }
                }
                _logger.LogDebug("Whisper processing completed, {Count} segments found", segments.Count);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // When cancelled, Whisper.net throws WhisperProcessingException instead of OperationCanceledException
                // Re-throw as OperationCanceledException so callers can properly handle cancellation
                _logger.LogInformation("Whisper processing cancelled by user");
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception procEx)
            {
                _logger.LogError(procEx, "Error during Whisper ProcessAsync");
                throw;
            }

            var transcription = string.Join(" ", segments);

            if (string.IsNullOrWhiteSpace(transcription))
            {
                _logger.LogWarning("Transcription result is empty");
                return new TranscriptionResult("No speech detected");
            }

            const float confidence = 1.0f;
            _logger.LogInformation("Transcription successful: {Text}", transcription);

            return new TranscriptionResult(transcription, confidence);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Transcription was cancelled");
            return new TranscriptionResult("Transcription cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription failed");
            return new TranscriptionResult($"Transcription error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream();
        await audioStream.CopyToAsync(memoryStream, cancellationToken);
        return await TranscribeAsync(memoryStream.ToArray(), cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _processor?.Dispose();
        _whisperFactory?.Dispose();
        _initLock.Dispose();
        _transcribeLock.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);

        _logger.LogDebug("WhisperNetTranscriber disposed");
    }
}
