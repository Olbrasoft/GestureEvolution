using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText.Service.Services;

/// <summary>
/// HTTP-based TTS control service implementation.
/// Communicates with EdgeTTS (port 5555) and VirtualAssistant (port 5055) services.
/// </summary>
public class TtsControlService : ITtsControlService
{
    private readonly ILogger<TtsControlService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _edgeTtsBaseUrl;
    private readonly string _virtualAssistantBaseUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="TtsControlService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClient">HTTP client for API calls.</param>
    /// <param name="configuration">Configuration for service URLs.</param>
    public TtsControlService(
        ILogger<TtsControlService> logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _edgeTtsBaseUrl = configuration?.GetValue<string>("EdgeTts:BaseUrl")
            ?? "http://localhost:5555";
        _virtualAssistantBaseUrl = configuration?.GetValue<string>("VirtualAssistant:BaseUrl")
            ?? "http://localhost:5055";
    }

    /// <inheritdoc/>
    public async Task StopAllSpeechAsync()
    {
        // Stop both TTS services in parallel
        var tasks = new[]
        {
            StopEdgeTtsAsync(),
            StopVirtualAssistantTtsAsync()
        };

        await Task.WhenAll(tasks);
    }

    /// <inheritdoc/>
    public async Task FlushQueueAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync($"{_virtualAssistantBaseUrl}/api/tts/flush-queue", null);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("TTS queue flush request sent successfully");
            }
            else
            {
                _logger.LogWarning("TTS queue flush request failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send TTS queue flush request");
        }
    }

    /// <inheritdoc/>
    public async Task<bool?> GetMuteStateAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_virtualAssistantBaseUrl}/api/mute");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("muted", out var mutedElement))
                {
                    return mutedElement.GetBoolean();
                }
            }
            else
            {
                _logger.LogWarning("VirtualAssistant mute state request failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get VirtualAssistant mute state");
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task SetMuteAsync(bool muted)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { Muted = muted }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync($"{_virtualAssistantBaseUrl}/api/mute", content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("VirtualAssistant mute state set to: {Muted}", muted);
            }
            else
            {
                _logger.LogWarning("VirtualAssistant mute request failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set VirtualAssistant mute state to {Muted}", muted);
        }
    }

    private async Task StopEdgeTtsAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync($"{_edgeTtsBaseUrl}/api/speech/stop", null);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("EdgeTTS speech stop request sent successfully");
            }
            else
            {
                _logger.LogWarning("EdgeTTS speech stop request failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send EdgeTTS speech stop request");
        }
    }

    private async Task StopVirtualAssistantTtsAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync($"{_virtualAssistantBaseUrl}/api/tts/stop", null);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("VirtualAssistant TTS stop request sent successfully");
            }
            else
            {
                _logger.LogWarning("VirtualAssistant TTS stop request failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send VirtualAssistant TTS stop request");
        }
    }

    /// <inheritdoc/>
    public async Task StartSpeechLockAsync(int? timeoutSeconds = null)
    {
        try
        {
            HttpContent? content = null;
            if (timeoutSeconds.HasValue)
            {
                content = new StringContent(
                    JsonSerializer.Serialize(new { TimeoutSeconds = timeoutSeconds.Value }),
                    Encoding.UTF8,
                    "application/json");
            }

            var response = await _httpClient.PostAsync(
                $"{_virtualAssistantBaseUrl}/api/speech-lock/start",
                content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Speech lock started on VirtualAssistant (timeout: {Timeout}s)",
                    timeoutSeconds ?? 30);
            }
            else
            {
                _logger.LogWarning("Speech lock start request failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // Don't fail recording if VA is not running
            _logger.LogDebug(ex, "Could not start speech lock on VirtualAssistant (may not be running)");
        }
    }

    /// <inheritdoc/>
    public async Task StopSpeechLockAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync(
                $"{_virtualAssistantBaseUrl}/api/speech-lock/stop",
                null);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Speech lock stopped on VirtualAssistant");
            }
            else
            {
                _logger.LogWarning("Speech lock stop request failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // Don't fail recording if VA is not running
            _logger.LogDebug(ex, "Could not stop speech lock on VirtualAssistant (may not be running)");
        }
    }
}
