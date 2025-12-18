using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.Audio;
using Olbrasoft.SpeechToText.Core.Interfaces;
using Olbrasoft.SpeechToText.Service.Services;
using Olbrasoft.SpeechToText.Speech;
using Olbrasoft.SpeechToText.TextInput;
using Olbrasoft.SpeechToText.GestureControl.Services;

// Disambiguate types that exist in multiple namespaces
using SttManualMuteService = Olbrasoft.SpeechToText.Service.Services.ManualMuteService;

namespace Olbrasoft.SpeechToText.Service;

/// <summary>
/// Extension methods for registering SpeechToText services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all SpeechToText services to the service collection.
    /// </summary>
    public static IServiceCollection AddSpeechToTextServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Get configuration values
        var ggmlModelPath = configuration.GetValue<string>("SpeechToTextDictation:GgmlModelPath")
            ?? Path.Combine(AppContext.BaseDirectory, "models", "ggml-medium.bin");
        var whisperLanguage = configuration.GetValue<string>("SpeechToTextDictation:WhisperLanguage") ?? "cs";

        // SignalR
        services.AddSignalR();

        // CORS (for web clients)
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
        });

        // PTT Notifier service
        services.AddSingleton<IPttNotifier, PttNotifier>();

        // Transcription history service (single-level history for repeat functionality)
        services.AddSingleton<ITranscriptionHistory, TranscriptionHistory>();

        // Manual mute service - register as concrete type for injection
        // Also register interface for backwards compatibility
        services.AddSingleton<SttManualMuteService>();
        services.AddSingleton<Olbrasoft.SpeechToText.Services.IManualMuteService>(sp =>
            sp.GetRequiredService<SttManualMuteService>());

        // Register services
        // Key simulator (for text input simulation)
        services.AddSingleton<IKeySimulator, UinputKeySimulator>();

        services.AddSingleton<IAudioRecorder>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PipeWireAudioRecorder>>();
            return new PipeWireAudioRecorder(logger);
        });

        services.AddSingleton<ISpeechTranscriber>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<WhisperNetTranscriber>>();
            return new WhisperNetTranscriber(logger, ggmlModelPath, whisperLanguage);
        });

        // Environment provider for display server detection
        services.AddSingleton<IEnvironmentProvider, SystemEnvironmentProvider>();

        // Text typer factory (injectable, testable)
        services.AddSingleton<ITextTyperFactory, TextTyperFactory>();

        // Auto-detect display server (X11/Wayland) and use appropriate text typer
        services.AddSingleton<ITextTyper>(sp =>
        {
            var factory = sp.GetRequiredService<ITextTyperFactory>();
            var logger = sp.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Detected display server: {DisplayServer}", factory.GetDisplayServerName());
            return factory.Create();
        });

        // Typing sound player for transcription feedback
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<TypingSoundPlayer>>();
            var soundsDirectory = Path.Combine(AppContext.BaseDirectory, "sounds");
            return TypingSoundPlayer.CreateFromDirectory(logger, soundsDirectory);
        });

        // Transcription tray service (not from DI - needs special lifecycle with GTK)
        services.AddSingleton<Tray.TranscriptionTrayService>();

        // Hallucination filter for Whisper transcriptions
        services.Configure<HallucinationFilterOptions>(
            configuration.GetSection(HallucinationFilterOptions.SectionName));
        services.AddSingleton<IHallucinationFilter, WhisperHallucinationFilter>();

        // Speech lock service (file-based lock to prevent TTS during recording)
        services.AddSingleton<ISpeechLockService, SpeechLockService>();

        // TTS control service (HTTP client for TTS and VirtualAssistant APIs)
        services.AddHttpClient<ITtsControlService, TtsControlService>();

        // Composite services (SRP refactoring - combines related dependencies)
        services.AddSingleton<ITranscriptionProcessor, TranscriptionProcessor>();
        services.AddSingleton<ITextOutputService, TextOutputService>();
        services.AddSingleton<IRecordingModeManager, RecordingModeManager>();

        // Recording workflow (extracted from DictationWorker for SRP)
        services.AddSingleton<IRecordingWorkflow, RecordingWorkflow>();

        // HTTP client for DictationWorker
        services.AddHttpClient<DictationWorker>();

        // Register worker as singleton first (so we can resolve it for interfaces)
        services.AddSingleton<DictationWorker>();
        services.AddHostedService<DictationWorker>(sp => sp.GetRequiredService<DictationWorker>());

        // Register interfaces pointing to the same DictationWorker instance
        services.AddSingleton<IRecordingStateProvider>(sp => sp.GetRequiredService<DictationWorker>());
        services.AddSingleton<IRecordingController>(sp => sp.GetRequiredService<DictationWorker>());

        // Gesture detection service (background service for hand tracking)
        services.AddSingleton<GestureDetectionService>();
        services.AddHostedService<GestureDetectionService>(sp => sp.GetRequiredService<GestureDetectionService>());

        // Bridge service to connect gesture detection with tray icons
        services.AddHostedService<GestureTrayBridgeService>();

        return services;
    }
}
