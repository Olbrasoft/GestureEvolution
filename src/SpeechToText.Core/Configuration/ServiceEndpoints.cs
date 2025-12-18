namespace Olbrasoft.SpeechToText.Core.Configuration;

/// <summary>
/// Configuration for service endpoints used across the application.
/// Centralizes all URL/port configuration to avoid magic numbers in code.
/// </summary>
public class ServiceEndpoints
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "ServiceEndpoints";

    /// <summary>
    /// Default port for the web server (API, SignalR, static files).
    /// </summary>
    public const int DefaultWebServerPort = 5050;

    /// <summary>
    /// Default port for EdgeTTS service.
    /// </summary>
    public const int DefaultEdgeTtsPort = 5555;

    /// <summary>
    /// Default port for VirtualAssistant service.
    /// </summary>
    public const int DefaultVirtualAssistantPort = 5055;

    /// <summary>
    /// Default port for logs viewer service.
    /// </summary>
    public const int DefaultLogsViewerPort = 5052;

    /// <summary>
    /// URL for EdgeTTS service.
    /// </summary>
    public string EdgeTts { get; set; } = $"http://localhost:{DefaultEdgeTtsPort}";

    /// <summary>
    /// URL for VirtualAssistant service.
    /// </summary>
    public string VirtualAssistant { get; set; } = $"http://localhost:{DefaultVirtualAssistantPort}";

    /// <summary>
    /// URL for logs viewer service.
    /// </summary>
    public string LogsViewer { get; set; } = $"http://127.0.0.1:{DefaultLogsViewerPort}";

    /// <summary>
    /// Port for the web server.
    /// </summary>
    public int WebServerPort { get; set; } = DefaultWebServerPort;
}
