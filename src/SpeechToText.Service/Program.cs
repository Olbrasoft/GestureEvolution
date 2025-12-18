using Olbrasoft.SpeechToText;
using Olbrasoft.SpeechToText.Audio;
using Olbrasoft.SpeechToText.Core.Configuration;
using Olbrasoft.SpeechToText.Service;
using Olbrasoft.SpeechToText.Service.Services;
using Olbrasoft.SpeechToText.Service.Tray;

// Load configuration early to get lock file paths
var earlyConfig = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var lockFilePath = earlyConfig["SystemPaths:PushToTalkLockFile"]
    ?? "/tmp/push-to-talk-dictation.lock";

// Single instance check
using var instanceLock = SingleInstanceLock.TryAcquire(lockFilePath);
if (!instanceLock.IsAcquired)
{
    Console.WriteLine("ERROR: Push-to-Talk Dictation is already running!");
    Console.WriteLine("Only one instance is allowed.");
    Environment.Exit(1);
    return;
}

var cts = new CancellationTokenSource();

var builder = WebApplication.CreateBuilder(args);

// Add all services using extension method
builder.Services.AddPushToTalkServices(builder.Configuration);

// Configure logging
builder.Logging.AddConsole();
builder.Logging.AddSystemdConsole();

var app = builder.Build();

app.UseCors();

// Serve static files from wwwroot (for remote control page)
app.UseStaticFiles();

// Map all endpoints using extension method
app.MapPushToTalkEndpoints();

// Get services for initialization
var pttNotifier = app.Services.GetRequiredService<IPttNotifier>();
var trayLogger = app.Services.GetRequiredService<ILogger<TranscriptionTrayService>>();
var typingSoundPlayer = app.Services.GetRequiredService<TypingSoundPlayer>();
var trayService = new TranscriptionTrayService(trayLogger, pttNotifier, typingSoundPlayer, builder.Configuration);

// Get web server port from configuration
var webPort = builder.Configuration.GetValue<int>("WebServer:Port", ServiceEndpoints.DefaultWebServerPort);

try
{
    // Initialize tray (must be on main thread for GTK)
    trayService.Initialize(() =>
    {
        Console.WriteLine("Quit requested from tray - stopping services...");
        cts.Cancel();
    });

    Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║            PushToTalk Dictation Service                    ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine("Transcription tray icon initialized");
    Console.WriteLine($"API listening on http://localhost:{webPort}");

    // Start WebApplication in background
    var hostTask = Task.Run(async () =>
    {
        try
        {
            await app.RunAsync(cts.Token);
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
        cts.Cancel();
        trayService.QuitMainLoop();
    };

    Console.WriteLine("Push-to-Talk running - tray icon active");
    Console.WriteLine("Press Ctrl+C or use tray menu to exit");
    Console.WriteLine();

    // Run GTK main loop (blocks until quit)
    trayService.RunMainLoop();

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
    trayService.Dispose();
    app.DisposeAsync().AsTask().Wait();
    cts.Dispose();
}

Console.WriteLine("Push-to-Talk stopped");
