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

var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║              SpeechToText Desktop Application                ║");
Console.WriteLine($"║                      Version: {version,-25}       ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();
logger.LogInformation("SpeechToText version {Version} starting", version);

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

// Create D-Bus tray icon (new implementation that bypasses GNOME icon caching)
var dbusTrayIconLogger = loggerFactory.CreateLogger<DBusTrayIcon>();
var iconsPath = Path.Combine(AppContext.BaseDirectory, "icons");
if (!Directory.Exists(iconsPath))
{
    iconsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "assets", "icons");
}
var dbusTrayIcon = new DBusTrayIcon(dbusTrayIconLogger, iconsPath, iconSize: 22);

// Create separate animated icon for transcription (second tray icon to bypass GNOME caching)
var animatedIconLogger = loggerFactory.CreateLogger<DBusAnimatedIcon>();
var animatedIcon = new DBusAnimatedIcon(animatedIconLogger, iconsPath, new[]
{
    "document-white-frame1",
    "document-white-frame2",
    "document-white-frame3",
    "document-white-frame4",
    "document-white-frame5"
}, iconSize: 22, intervalMs: 150);

var cts = new CancellationTokenSource();

try
{
    // Initialize D-Bus tray icon
    await dbusTrayIcon.InitializeAsync();
    await animatedIcon.InitializeAsync();
    
    if (dbusTrayIcon.IsActive)
    {
        Console.WriteLine("D-Bus tray icon initialized");
        
        // Set initial icon
        dbusTrayIcon.SetIcon("trigger-speech-to-text");
        dbusTrayIcon.SetTooltip("Speech to Text - Idle");
        
        // Handle state changes from DictationService
        dictationService.StateChanged += async (_, state) =>
        {
            switch (state)
            {
                case DictationState.Idle:
                    animatedIcon.Hide();
                    dbusTrayIcon.SetIcon("trigger-speech-to-text");
                    dbusTrayIcon.SetTooltip("Speech to Text - Idle");
                    break;
                case DictationState.Recording:
                    animatedIcon.Hide();
                    dbusTrayIcon.SetIcon("trigger-speech-to-text-recording");
                    dbusTrayIcon.SetTooltip("Speech to Text - Recording...");
                    break;
                case DictationState.Transcribing:
                    dbusTrayIcon.SetIcon("trigger-speech-to-text");
                    dbusTrayIcon.SetTooltip("Speech to Text - Transcribing...");
                    await animatedIcon.ShowAsync();
                    break;
            }
        };
        
        // Handle click on tray icon (toggle recording)
        dbusTrayIcon.OnClicked += () =>
        {
            logger.LogInformation("Tray icon clicked");
        };

        // Handle Quit menu item
        dbusTrayIcon.OnQuitRequested += () =>
        {
            logger.LogInformation("Quit requested from tray menu");
            Console.WriteLine("\nQuit requested - shutting down...");
            cts.Cancel();
        };

        // Handle About menu item
        dbusTrayIcon.OnAboutRequested += () =>
        {
            logger.LogInformation("About dialog requested");
            ShowAboutDialog(version);
        };
    }
    else
    {
        logger.LogWarning("D-Bus tray icon failed to initialize, continuing without tray icon");
    }

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
    Console.WriteLine("Press Ctrl+C to exit");
    Console.WriteLine();

    // Handle Ctrl+C
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Console.WriteLine("\nCtrl+C pressed - shutting down...");
        cts.Cancel();
    };

    // Keep the application running (no GTK main loop needed for D-Bus)
    try
    {
        await Task.Delay(Timeout.Infinite, cts.Token);
    }
    catch (OperationCanceledException)
    {
        // Normal shutdown
    }
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
    animatedIcon.Dispose();
    dbusTrayIcon.Dispose();
    lockFile?.Dispose();

    try
    {
        if (File.Exists(lockFilePath))
            File.Delete(lockFilePath);
    }
    catch { }
}

Console.WriteLine("SpeechToText stopped");

/// <summary>
/// Shows the About dialog using zenity (GNOME dialog tool).
/// </summary>
static void ShowAboutDialog(string version)
{
    try
    {
        var aboutText = $"Speech to Text\n\n" +
                        $"Version: {version}\n\n" +
                        $"Voice transcription using Whisper AI.\n" +
                        $"Press CapsLock to start dictation.\n\n" +
                        $"https://github.com/Olbrasoft/SpeechToText";

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "zenity",
            Arguments = $"--info --title=\"About Speech to Text\" --text=\"{aboutText.Replace("\"", "\\\"")}\" --width=400",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        System.Diagnostics.Process.Start(startInfo);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Could not show About dialog: {ex.Message}");
        Console.WriteLine($"Speech to Text v{version}");
        Console.WriteLine("https://github.com/Olbrasoft/SpeechToText");
    }
}
