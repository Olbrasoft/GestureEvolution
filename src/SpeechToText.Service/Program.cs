using System.Diagnostics;
using Olbrasoft.SpeechToText.Speech;
using Olbrasoft.SpeechToText;
using Olbrasoft.SpeechToText.Service;
using Olbrasoft.SpeechToText.Service.Hubs;
using Olbrasoft.SpeechToText.Audio;
using Olbrasoft.SpeechToText.Service.Services;
using Olbrasoft.SpeechToText.Service.Tray;
using Olbrasoft.SpeechToText.TextInput;

// Disambiguate types that exist in multiple namespaces
using PttManualMuteService = Olbrasoft.SpeechToText.Service.Services.ManualMuteService;
using PttEvdevKeyboardMonitor = Olbrasoft.SpeechToText.EvdevKeyboardMonitor;

// Load configuration early to get lock file paths
var earlyConfig = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var lockFilePath = earlyConfig["SystemPaths:PushToTalkLockFile"]
    ?? "/tmp/push-to-talk-dictation.lock";
var speechLockFilePath = earlyConfig["SystemPaths:SpeechLockFile"]
    ?? "/tmp/speech-lock";

// Single instance lock
FileStream? _lockFile = null;

// Single instance check - try to acquire exclusive lock
if (!TryAcquireSingleInstanceLock())
{
    Console.WriteLine("ERROR: Push-to-Talk Dictation is already running!");
    Console.WriteLine("Only one instance is allowed.");
    Environment.Exit(1);
    return;
}

// Static fields for GTK integration
WebApplication? _app = null;
TranscriptionTrayService? _trayService = null;
CancellationTokenSource? _cts = null;

_cts = new CancellationTokenSource();

var builder = WebApplication.CreateBuilder(args);

// Get configuration values
var keyboardDevice = builder.Configuration.GetValue<string?>("PushToTalkDictation:KeyboardDevice");
var ggmlModelPath = builder.Configuration.GetValue<string>("PushToTalkDictation:GgmlModelPath") 
    ?? Path.Combine(AppContext.BaseDirectory, "models", "ggml-medium.bin");
var whisperLanguage = builder.Configuration.GetValue<string>("PushToTalkDictation:WhisperLanguage") ?? "cs";

// SignalR
builder.Services.AddSignalR();

// CORS (for web clients)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// PTT Notifier service
builder.Services.AddSingleton<IPttNotifier, PttNotifier>();

// Transcription history service (single-level history for repeat functionality)
builder.Services.AddSingleton<ITranscriptionHistory, TranscriptionHistory>();

// Manual mute service (ScrollLock) - register as concrete type for injection
// Also register interface for backwards compatibility
builder.Services.AddSingleton<PttManualMuteService>();
builder.Services.AddSingleton<Olbrasoft.SpeechToText.Services.IManualMuteService>(sp => sp.GetRequiredService<PttManualMuteService>());

// Register services
builder.Services.AddSingleton<IKeyboardMonitor>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<PttEvdevKeyboardMonitor>>();
    return new PttEvdevKeyboardMonitor(logger, keyboardDevice);
});

// Key simulator (ISP: separated from keyboard monitoring)
builder.Services.AddSingleton<IKeySimulator, UinputKeySimulator>();

builder.Services.AddSingleton<IAudioRecorder>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<AlsaAudioRecorder>>();
    return new AlsaAudioRecorder(logger);
});

builder.Services.AddSingleton<ISpeechTranscriber>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<WhisperNetTranscriber>>();
    return new WhisperNetTranscriber(logger, ggmlModelPath, whisperLanguage);
});

// Environment provider for display server detection
builder.Services.AddSingleton<IEnvironmentProvider, SystemEnvironmentProvider>();

// Text typer factory (injectable, testable)
builder.Services.AddSingleton<ITextTyperFactory, TextTyperFactory>();

// Auto-detect display server (X11/Wayland) and use appropriate text typer
builder.Services.AddSingleton<ITextTyper>(sp =>
{
    var factory = sp.GetRequiredService<ITextTyperFactory>();
    var logger = sp.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Detected display server: {DisplayServer}", factory.GetDisplayServerName());
    return factory.Create();
});

// Typing sound player for transcription feedback
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<TypingSoundPlayer>>();
    var soundsDirectory = Path.Combine(AppContext.BaseDirectory, "sounds");
    return TypingSoundPlayer.CreateFromDirectory(logger, soundsDirectory);
});

// Transcription tray service (not from DI - needs special lifecycle with GTK)
builder.Services.AddSingleton<TranscriptionTrayService>();

// Hallucination filter for Whisper transcriptions
builder.Services.Configure<HallucinationFilterOptions>(
    builder.Configuration.GetSection(HallucinationFilterOptions.SectionName));
builder.Services.AddSingleton<IHallucinationFilter, WhisperHallucinationFilter>();

// Speech lock service (file-based lock to prevent TTS during recording)
builder.Services.AddSingleton<ISpeechLockService, SpeechLockService>();

// TTS control service (HTTP client for TTS and VirtualAssistant APIs)
builder.Services.AddHttpClient<ITtsControlService, TtsControlService>();

// Bluetooth mouse monitor (remote push-to-talk trigger)
var bluetoothTripleClickCommand = builder.Configuration.GetValue<string?>("BluetoothMouse:TripleClickCommand");
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<BluetoothMouseMonitor>>();
    var keyboardMonitor = sp.GetRequiredService<IKeyboardMonitor>();
    var keySimulator = sp.GetRequiredService<IKeySimulator>();
    return new BluetoothMouseMonitor(logger, keyboardMonitor, keySimulator, "BluetoothMouse3600", bluetoothTripleClickCommand);
});

// USB Optical Mouse monitor (secondary push-to-talk trigger)
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<UsbMouseMonitor>>();
    var keyboardMonitor = sp.GetRequiredService<IKeyboardMonitor>();
    var keySimulator = sp.GetRequiredService<IKeySimulator>();
    return new UsbMouseMonitor(logger, keyboardMonitor, keySimulator);
});

// Register worker
builder.Services.AddHostedService<DictationWorker>();

// Configure logging
builder.Logging.AddConsole();
builder.Logging.AddSystemdConsole();

_app = builder.Build();

_app.UseCors();

// Map SignalR hub
_app.MapHub<PttHub>("/hubs/ptt");

// Health check endpoint
_app.MapGet("/", () => Results.Ok(new { service = "Olbrasoft.SpeechToText", status = "running" }));

// Repeat last transcription endpoint - copies last text to clipboard
_app.MapPost("/api/ptt/repeat", async (ITranscriptionHistory history, ILogger<Program> logger) =>
{
    var lastText = history.LastText;
    
    if (string.IsNullOrEmpty(lastText))
    {
        logger.LogWarning("Repeat requested but no transcription history available");
        return Results.NotFound(new { success = false, message = "No transcription history available" });
    }
    
    try
    {
        // Copy to clipboard using wl-copy
        var wlCopyProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "wl-copy",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        wlCopyProcess.Start();
        await wlCopyProcess.StandardInput.WriteAsync(lastText);
        wlCopyProcess.StandardInput.Close();
        await wlCopyProcess.WaitForExitAsync();

        if (wlCopyProcess.ExitCode != 0)
        {
            var error = await wlCopyProcess.StandardError.ReadToEndAsync();
            logger.LogError("wl-copy failed with exit code {ExitCode}: {Error}", wlCopyProcess.ExitCode, error);
            return Results.StatusCode(500);
        }
        
        logger.LogInformation("Repeat: copied last transcription to clipboard ({Length} chars)", lastText.Length);
        return Results.Ok(new { success = true, text = lastText, copiedToClipboard = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to copy text to clipboard");
        return Results.StatusCode(500);
    }
});

// Get tray service from DI
var pttNotifier = _app.Services.GetRequiredService<IPttNotifier>();
var trayLogger = _app.Services.GetRequiredService<ILogger<TranscriptionTrayService>>();
var typingSoundPlayer = _app.Services.GetRequiredService<TypingSoundPlayer>();
_trayService = new TranscriptionTrayService(trayLogger, pttNotifier, typingSoundPlayer);

// Start Bluetooth mouse monitor (remote push-to-talk trigger)
BluetoothMouseMonitor? _bluetoothMouseMonitor = _app.Services.GetRequiredService<BluetoothMouseMonitor>();
_ = _bluetoothMouseMonitor.StartMonitoringAsync(_cts!.Token);
Console.WriteLine("Bluetooth mouse monitor started (LEFT=CapsLock, 2xLEFT=ESC, 3xLEFT=OpenCode, 2xRIGHT=Ctrl+Shift+V, 3xRIGHT=Ctrl+C, MIDDLE=Enter)");

// Start USB Optical Mouse monitor (secondary push-to-talk trigger)
UsbMouseMonitor? _usbMouseMonitor = _app.Services.GetRequiredService<UsbMouseMonitor>();
_ = _usbMouseMonitor.StartMonitoringAsync(_cts!.Token);
Console.WriteLine("USB mouse monitor started (LEFT=CapsLock, 2xLEFT=ESC, 2xRIGHT=Ctrl+Shift+V, 3xRIGHT=Ctrl+C)");

try
{
    // Initialize tray (must be on main thread for GTK)
    _trayService.Initialize(() =>
    {
        Console.WriteLine("Quit requested from tray - stopping services...");
        _cts?.Cancel();
    });
    
    Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║            SpeechToText Dictation Service                    ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine("Transcription tray icon initialized");
    Console.WriteLine("API listening on http://localhost:5050");

    // Start WebApplication in background
    var hostTask = Task.Run(async () =>
    {
        try
        {
            await _app.RunAsync(_cts!.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    });

    // Handle Ctrl+C
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Console.WriteLine("\nCtrl+C pressed - shutting down...");
        _cts?.Cancel();
        _trayService?.QuitMainLoop();
    };

    Console.WriteLine("Push-to-Talk running - tray icon active");
    Console.WriteLine("Press Ctrl+C or use tray menu to exit");
    Console.WriteLine();

    // Run GTK main loop (blocks until quit)
    _trayService.RunMainLoop();

    // Wait for host to finish
    hostTask.Wait(TimeSpan.FromSeconds(5));
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Environment.Exit(1);
}
finally
{
    _bluetoothMouseMonitor?.Dispose();
    _usbMouseMonitor?.Dispose();
    _trayService?.Dispose();
    _app?.DisposeAsync().AsTask().Wait();
    _cts?.Dispose();
    ReleaseSingleInstanceLock();
}

Console.WriteLine("Push-to-Talk stopped");

/// <summary>
/// Tries to acquire an exclusive lock to ensure only one instance runs.
/// </summary>
bool TryAcquireSingleInstanceLock()
{
    try
    {
        _lockFile = new FileStream(
            lockFilePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        // Write PID to lock file for debugging
        var pid = Environment.ProcessId.ToString();
        _lockFile.SetLength(0);
        var bytes = System.Text.Encoding.UTF8.GetBytes(pid);
        _lockFile.Write(bytes, 0, bytes.Length);
        _lockFile.Flush();

        return true;
    }
    catch (IOException)
    {
        // Lock file is held by another process
        return false;
    }
}

/// <summary>
/// Releases the single instance lock.
/// </summary>
void ReleaseSingleInstanceLock()
{
    try
    {
        _lockFile?.Dispose();
        _lockFile = null;

        if (File.Exists(lockFilePath))
        {
            File.Delete(lockFilePath);
        }
    }
    catch
    {
        // Ignore cleanup errors
    }
}
