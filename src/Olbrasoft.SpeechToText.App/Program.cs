using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText;
using Olbrasoft.SpeechToText.App;
using Olbrasoft.SpeechToText.Speech;
using Olbrasoft.SpeechToText.TextInput;

// Load configuration
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

// Get configuration values
var keyboardDevice = config.GetValue<string?>("Dictation:KeyboardDevice");
var ggmlModelPath = config.GetValue<string>("Dictation:GgmlModelPath")
    ?? Path.Combine(AppContext.BaseDirectory, "models", "ggml-medium.bin");
var whisperLanguage = config.GetValue<string>("Dictation:WhisperLanguage") ?? "cs";
var triggerKeyName = config.GetValue<string>("Dictation:TriggerKey") ?? "CapsLock";
var triggerKey = Enum.TryParse<KeyCode>(triggerKeyName, ignoreCase: true, out var key) ? key : KeyCode.CapsLock;
var cancelKeyName = config.GetValue<string>("Dictation:CancelKey") ?? "Escape";
var cancelKey = Enum.TryParse<KeyCode>(cancelKeyName, ignoreCase: true, out var ckey) ? ckey : KeyCode.Escape;
var transcriptionSoundPath = config.GetValue<string?>("Dictation:TranscriptionSoundPath");
var showTranscriptionAnimation = config.GetValue<bool>("Dictation:ShowTranscriptionAnimation");
var textFiltersPath = config.GetValue<string?>("Dictation:TextFiltersPath");

// Setup logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<Program>();

// Single instance lock
var lockFilePath = "/tmp/speech-to-text.lock";
FileStream? lockFile = null;

try
{
    lockFile = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var pidBytes = System.Text.Encoding.UTF8.GetBytes(Environment.ProcessId.ToString());
    lockFile.Write(pidBytes, 0, pidBytes.Length);
    lockFile.Flush();
}
catch (IOException)
{
    Console.WriteLine("ERROR: SpeechToText is already running!");
    Console.WriteLine("Only one instance is allowed.");
    Environment.Exit(1);
    return;
}

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║              SpeechToText Desktop Application                ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Create services
var keyboardMonitorLogger = loggerFactory.CreateLogger<EvdevKeyboardMonitor>();
var keyboardMonitor = new EvdevKeyboardMonitor(keyboardMonitorLogger, keyboardDevice);

var audioRecorderLogger = loggerFactory.CreateLogger<AlsaAudioRecorder>();
var audioRecorder = new AlsaAudioRecorder(audioRecorderLogger);

var transcriberLogger = loggerFactory.CreateLogger<WhisperNetTranscriber>();
ISpeechTranscriber speechTranscriber;

try
{
    speechTranscriber = new WhisperNetTranscriber(transcriberLogger, ggmlModelPath, whisperLanguage);
    logger.LogInformation("Whisper model loaded: {Path}", ggmlModelPath);
}
catch (FileNotFoundException)
{
    logger.LogError("Whisper model not found: {Path}", ggmlModelPath);
    Console.WriteLine($"ERROR: Whisper model not found at: {ggmlModelPath}");
    Console.WriteLine("Please download a model and update appsettings.json");
    Environment.Exit(1);
    return;
}

var textTyper = TextTyperFactory.Create(loggerFactory);
logger.LogInformation("Text typer: {DisplayServer}", TextTyperFactory.GetDisplayServerName());

// Create typing sound player (for audio feedback during transcription)
TypingSoundPlayer? typingSoundPlayer = null;
if (!string.IsNullOrWhiteSpace(transcriptionSoundPath))
{
    var fullSoundPath = Path.IsPathRooted(transcriptionSoundPath)
        ? transcriptionSoundPath
        : Path.Combine(AppContext.BaseDirectory, transcriptionSoundPath);
    var typingSoundLogger = loggerFactory.CreateLogger<TypingSoundPlayer>();
    typingSoundPlayer = new TypingSoundPlayer(typingSoundLogger, fullSoundPath);
}

// Create text filter (for removing Whisper hallucinations)
TextFilter? textFilter = null;
if (!string.IsNullOrWhiteSpace(textFiltersPath))
{
    var fullFiltersPath = Path.IsPathRooted(textFiltersPath)
        ? textFiltersPath
        : Path.Combine(AppContext.BaseDirectory, textFiltersPath);
    var textFilterLogger = loggerFactory.CreateLogger<TextFilter>();
    textFilter = new TextFilter(textFilterLogger, fullFiltersPath);
}

var dictationServiceLogger = loggerFactory.CreateLogger<DictationService>();
var dictationService = new DictationService(
    dictationServiceLogger,
    keyboardMonitor,
    audioRecorder,
    speechTranscriber,
    textTyper,
    typingSoundPlayer,
    textFilter,
    triggerKey,
    cancelKey);

var trayIconLogger = loggerFactory.CreateLogger<TrayIcon>();
var trayIcon = new TrayIcon(trayIconLogger, dictationService, showTranscriptionAnimation);

var cts = new CancellationTokenSource();

try
{
    // Initialize tray icon (must be on main thread for GTK)
    trayIcon.Initialize();
    Console.WriteLine("Tray icon initialized");

    // Start keyboard monitoring in background
    _ = Task.Run(async () =>
    {
        try
        {
            await dictationService.StartMonitoringAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Keyboard monitoring failed");
        }
    });

    Console.WriteLine($"Keyboard monitoring started ({triggerKey} to trigger)");
    Console.WriteLine("Press Ctrl+C or use tray menu to exit");
    Console.WriteLine();

    // Handle Ctrl+C
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Console.WriteLine("\nCtrl+C pressed - shutting down...");
        cts.Cancel();
        trayIcon.QuitMainLoop();
    };

    // Run GTK main loop (blocks until quit)
    trayIcon.RunMainLoop();
}
catch (Exception ex)
{
    logger.LogError(ex, "Application error");
    Console.WriteLine($"Error: {ex.Message}");
    Environment.Exit(1);
}
finally
{
    cts.Cancel();
    dictationService.Dispose();
    trayIcon.Dispose();
    lockFile?.Dispose();

    try
    {
        if (File.Exists(lockFilePath))
            File.Delete(lockFilePath);
    }
    catch { }
}

Console.WriteLine("SpeechToText stopped");
