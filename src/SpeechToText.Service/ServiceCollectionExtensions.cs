using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.Audio;
using Olbrasoft.SpeechToText.Core.Interfaces;
using Olbrasoft.SpeechToText.Service.Services;
using Olbrasoft.SpeechToText.Speech;
using Olbrasoft.SpeechToText.TextInput;

// Disambiguate types that exist in multiple namespaces
using SttManualMuteService = Olbrasoft.SpeechToText.Service.Services.ManualMuteService;
using SttEvdevKeyboardMonitor = Olbrasoft.SpeechToText.EvdevKeyboardMonitor;

namespace Olbrasoft.SpeechToText.Service;

/// <summary>
/// Extension methods for registering PushToTalk services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all PushToTalk services to the service collection.
    /// </summary>
    public static IServiceCollection AddPushToTalkServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Get configuration values
        var keyboardDevice = configuration.GetValue<string?>("PushToTalkDictation:KeyboardDevice");
        var ggmlModelPath = configuration.GetValue<string>("PushToTalkDictation:GgmlModelPath")
            ?? Path.Combine(AppContext.BaseDirectory, "models", "ggml-medium.bin");
        var whisperLanguage = configuration.GetValue<string>("PushToTalkDictation:WhisperLanguage") ?? "cs";

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

        // Manual mute service (ScrollLock) - register as concrete type for injection
        // Also register interface for backwards compatibility
        services.AddSingleton<SttManualMuteService>();
        services.AddSingleton<Olbrasoft.SpeechToText.Services.IManualMuteService>(sp =>
            sp.GetRequiredService<SttManualMuteService>());

        // Register services
        services.AddSingleton<IKeyboardMonitor>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SttEvdevKeyboardMonitor>>();
            return new SttEvdevKeyboardMonitor(logger, keyboardDevice);
        });

        // Key simulator (ISP: separated from keyboard monitoring)
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

        return services;
    }
}
