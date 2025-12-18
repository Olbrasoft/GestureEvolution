using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.Core.Configuration;

namespace Olbrasoft.SpeechToText.App.Services;

/// <summary>
/// HTTP client for VirtualAssistant speech-lock coordination.
/// Gracefully handles cases when VirtualAssistant is not running.
/// </summary>
public class VirtualAssistantClient : IVirtualAssistantClient
{
    private readonly ILogger<VirtualAssistantClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly int _timeoutSeconds;

    public VirtualAssistantClient(
        ILogger<VirtualAssistantClient> logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;

        var endpoints = new ServiceEndpoints();
        configuration.GetSection(ServiceEndpoints.SectionName).Bind(endpoints);

        _baseUrl = configuration.GetValue<string>("VirtualAssistant:BaseUrl")
            ?? endpoints.VirtualAssistant;
        _timeoutSeconds = configuration.GetValue<int>("VirtualAssistant:TimeoutSeconds", 30);
    }

    public async Task NotifyRecordingStartedAsync(CancellationToken ct = default)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { TimeoutSeconds = _timeoutSeconds }),
                Encoding.UTF8,
                "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2)); // Don't block recording for more than 2s

            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/api/speech-lock/start",
                content,
                cts.Token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("🔒 VirtualAssistant speech lock STARTED");
            }
            else
            {
                _logger.LogWarning("VirtualAssistant speech-lock start failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("VirtualAssistant notification timed out (recording not blocked)");
        }
        catch (HttpRequestException ex)
        {
            // VirtualAssistant not running - that's OK, continue recording
            _logger.LogDebug("VirtualAssistant not reachable: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not notify VirtualAssistant (may not be running)");
        }
    }

    public async Task NotifyRecordingStoppedAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2)); // Don't block transcription for more than 2s

            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/api/speech-lock/stop",
                null,
                cts.Token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("🔓 VirtualAssistant speech lock STOPPED");
            }
            else
            {
                _logger.LogWarning("VirtualAssistant speech-lock stop failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("VirtualAssistant notification timed out");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug("VirtualAssistant not reachable: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not notify VirtualAssistant");
        }
    }
}
