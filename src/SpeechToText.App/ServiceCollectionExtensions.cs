using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.App.Services;
using Olbrasoft.SpeechToText.Audio;
using Olbrasoft.SpeechToText.Speech;
using Olbrasoft.SpeechToText.TextInput;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// Extension methods for registering dictation services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core dictation services to the service collection.
    /// </summary>
    public static IServiceCollection AddDictationServices(
        this IServiceCollection services,
        DictationOptions options,
        IConfiguration configuration)
    {
        // VirtualAssistant client for TTS coordination
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IVirtualAssistantClient>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<VirtualAssistantClient>>();
            var httpClient = sp.GetRequiredService<HttpClient>();
            return new VirtualAssistantClient(logger, httpClient, configuration);
        });

        // Keyboard monitor
        services.AddSingleton<IKeyboardMonitor>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<EvdevKeyboardMonitor>>();
            return new EvdevKeyboardMonitor(logger, options.KeyboardDevice);
        });

        // Audio recorder
        services.AddSingleton<IAudioRecorder>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PipeWireAudioRecorder>>();
            return new PipeWireAudioRecorder(logger);
        });

        // Speech transcriber
        services.AddSingleton<ISpeechTranscriber>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<WhisperNetTranscriber>>();
            var modelPath = options.GetFullGgmlModelPath();
            return new WhisperNetTranscriber(logger, modelPath, options.WhisperLanguage);
        });

        // Environment provider for display server detection
        services.AddSingleton<IEnvironmentProvider, SystemEnvironmentProvider>();

        // Text typer factory (injectable, testable)
        services.AddSingleton<ITextTyperFactory, TextTyperFactory>();

        // Text typer (auto-detect display server)
        services.AddSingleton<ITextTyper>(sp =>
        {
            var factory = sp.GetRequiredService<ITextTyperFactory>();
            return factory.Create();
        });

        // Optional: Typing sound player
        var soundPath = options.GetFullTranscriptionSoundPath();
        if (!string.IsNullOrWhiteSpace(soundPath))
        {
            services.AddSingleton(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<TypingSoundPlayer>>();
                return new TypingSoundPlayer(logger, soundPath);
            });
        }

        // Optional: Text filter
        var filtersPath = options.GetFullTextFiltersPath();
        if (!string.IsNullOrWhiteSpace(filtersPath))
        {
            services.AddSingleton(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<TextFilter>>();
                return new TextFilter(logger, filtersPath);
            });
        }

        // Transcription coordinator (combines speech transcription + sound feedback)
        services.AddSingleton<ITranscriptionCoordinator>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<TranscriptionCoordinator>>();
            var transcriber = sp.GetRequiredService<ISpeechTranscriber>();
            var soundPlayer = sp.GetService<TypingSoundPlayer>();
            return new TranscriptionCoordinator(logger, transcriber, soundPlayer);
        });

        // Text output handler (combines text filtering + typing)
        services.AddSingleton<ITextOutputHandler>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<TextOutputHandler>>();
            var textTyper = sp.GetRequiredService<ITextTyper>();
            var textFilter = sp.GetService<TextFilter>();
            return new TextOutputHandler(logger, textTyper, textFilter);
        });

        // Dictation service (orchestrates recording, transcription, and output)
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DictationService>>();
            var keyboardMonitor = sp.GetRequiredService<IKeyboardMonitor>();
            var audioRecorder = sp.GetRequiredService<IAudioRecorder>();
            var transcriptionCoordinator = sp.GetRequiredService<ITranscriptionCoordinator>();
            var textOutputHandler = sp.GetRequiredService<ITextOutputHandler>();
            var vaClient = sp.GetService<IVirtualAssistantClient>();

            return new DictationService(
                logger,
                keyboardMonitor,
                audioRecorder,
                transcriptionCoordinator,
                textOutputHandler,
                vaClient,
                options.GetTriggerKeyCode(),
                options.GetCancelKeyCode());
        });

        return services;
    }

    /// <summary>
    /// Adds tray icon services to the service collection.
    /// </summary>
    public static IServiceCollection AddTrayServices(
        this IServiceCollection services,
        DictationOptions options,
        string iconsPath)
    {
        // Main tray icon (uses unique D-Bus path to avoid auto-detection - issue #62)
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DBusTrayIcon>>();
            return new DBusTrayIcon(logger, iconsPath, options.IconSize);
        });

        // Animated icon for transcription (shows NEXT TO main icon, uses unique path - issue #62)
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DBusAnimatedIcon>>();
            return new DBusAnimatedIcon(
                logger,
                iconsPath,
                options.AnimationFrames,
                options.IconSize,
                options.AnimationIntervalMs);
        });

        return services;
    }
}
