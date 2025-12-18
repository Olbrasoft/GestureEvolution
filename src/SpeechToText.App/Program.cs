using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.App;
using Olbrasoft.SpeechToText.App.Hubs;
using Olbrasoft.SpeechToText.Core.Configuration;
using Olbrasoft.SpeechToText.Core.Extensions;
using Olbrasoft.SpeechToText.TextInput;

// Single instance check
using var instanceLock = SingleInstanceLock.TryAcquire();
if (!instanceLock.IsAcquired)
{
    Console.WriteLine("ERROR: PushToTalk is already running!");
    Console.WriteLine("Only one instance is allowed.");
    Environment.Exit(1);
    return;
}

// Load configuration
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

var options = new DictationOptions();
config.GetSection(DictationOptions.SectionName).Bind(options);

// Get port from config or use default
var webPort = config.GetValue<int>("WebServer:Port", ServiceEndpoints.DefaultWebServerPort);

// Setup logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<Program>();

// Print banner
var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║              PushToTalk Desktop Application                ║");
Console.WriteLine($"║                      Version: {version,-25}       ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();
logger.LogInformation("PushToTalk version {Version} starting", version);

// Validate Whisper model exists
var modelPath = options.GetFullGgmlModelPath();
if (!File.Exists(modelPath))
{
    logger.LogError("Whisper model not found: {Path}", modelPath);
    Console.WriteLine($"ERROR: Whisper model not found at: {modelPath}");
    Console.WriteLine("Please download a model and update appsettings.json");
    Environment.Exit(1);
    return;
}

// Find icons path
var iconsPath = options.IconsPath ?? IconsPathResolver.FindIconsPath(logger);

// Build services using standard ServiceCollection
var services = new ServiceCollection();
services.AddSingleton(loggerFactory);
services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
services.AddDictationServices(options, config);
services.AddTrayServices(options, iconsPath);

using var serviceProvider = services.BuildServiceProvider();

// Get services
var dictationService = serviceProvider.GetRequiredService<DictationService>();
var dbusTrayIcon = serviceProvider.GetRequiredService<DBusTrayIcon>();
var animatedIcon = serviceProvider.GetRequiredService<DBusAnimatedIcon>();

logger.LogInformation("Whisper model loaded: {Path}", modelPath);
var textTyperFactory = serviceProvider.GetRequiredService<Olbrasoft.SpeechToText.TextInput.ITextTyperFactory>();
logger.LogInformation("Text typer: {DisplayServer}", textTyperFactory.GetDisplayServerName());

var cts = new CancellationTokenSource();

// Build web application for SignalR and remote control
var webBuilder = WebApplication.CreateBuilder();
webBuilder.WebHost.UseUrls($"http://0.0.0.0:{webPort}");
webBuilder.Logging.ClearProviders();
webBuilder.Logging.AddConsole();
webBuilder.Logging.SetMinimumLevel(LogLevel.Warning); // Reduce web server noise

// Add SignalR
webBuilder.Services.AddSignalR();

// Add CORS for remote access
webBuilder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Register DictationService as singleton (same instance)
webBuilder.Services.AddSingleton(dictationService);

// Register ITextTyper for the SignalR hub
var textTyper = serviceProvider.GetRequiredService<ITextTyper>();
webBuilder.Services.AddSingleton(textTyper);

var webApp = webBuilder.Build();

// Configure middleware
webApp.UseCors();

// Serve static files from wwwroot
var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    webApp.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(wwwrootPath)
    });
}

// Map SignalR hub
webApp.MapHub<DictationHub>("/hubs/dictation");

// Map status endpoint
webApp.MapGet("/api/status", () => new
{
    IsRecording = dictationService.State == DictationState.Recording,
    IsTranscribing = dictationService.State == DictationState.Transcribing,
    State = dictationService.State.ToString()
});

// Map control endpoints
webApp.MapPost("/api/recording/start", async () =>
{
    if (dictationService.State == DictationState.Idle)
    {
        await dictationService.StartDictationAsync();
        return Results.Ok(new { Success = true });
    }
    return Results.BadRequest(new { Success = false, Error = "Already recording or transcribing" });
});

webApp.MapPost("/api/recording/stop", async () =>
{
    if (dictationService.State == DictationState.Recording)
    {
        await dictationService.StopDictationAsync();
        return Results.Ok(new { Success = true });
    }
    return Results.BadRequest(new { Success = false, Error = "Not recording" });
});

webApp.MapPost("/api/recording/toggle", async () =>
{
    if (dictationService.State == DictationState.Idle)
    {
        await dictationService.StartDictationAsync();
        return Results.Ok(new { Success = true, IsRecording = true });
    }
    else if (dictationService.State == DictationState.Recording)
    {
        await dictationService.StopDictationAsync();
        return Results.Ok(new { Success = true, IsRecording = false });
    }
    return Results.BadRequest(new { Success = false, Error = "Transcription in progress" });
});

webApp.MapPost("/api/recording/cancel", () =>
{
    if (dictationService.State == DictationState.Transcribing)
    {
        dictationService.CancelTranscription();
        return Results.Ok(new { Success = true });
    }
    return Results.BadRequest(new { Success = false, Error = "Not transcribing" });
});

// Get SignalR hub context for broadcasting
var hubContext = webApp.Services.GetRequiredService<IHubContext<DictationHub>>();

try
{
    // Pre-set initial icon BEFORE initialization so it's available during D-Bus registration
    // (CreateTrayIconAsync uses _currentIcon when registering with StatusNotifierWatcher)
    dbusTrayIcon.SetIcon("trigger-ptt");
    dbusTrayIcon.SetTooltip("Push To Talk - Idle");

    // Initialize D-Bus tray icons (main + animated use unique paths to avoid duplicate detection - issue #62)
    await dbusTrayIcon.InitializeAsync();
    await animatedIcon.InitializeAsync();

    if (dbusTrayIcon.IsActive)
    {
        Console.WriteLine("D-Bus tray icon initialized");

        // Handle state changes from DictationService
        // Main icon stays visible, animated icon shows NEXT TO it during transcription (issue #62)
        dictationService.StateChanged += async (_, state) =>
        {
            // Update tray icon
            switch (state)
            {
                case DictationState.Idle:
                    animatedIcon.Hide();
                    dbusTrayIcon.SetIcon("trigger-ptt");
                    dbusTrayIcon.SetTooltip("Push To Talk - Idle");
                    break;
                case DictationState.Recording:
                    animatedIcon.Hide();
                    dbusTrayIcon.SetIcon("trigger-ptt-recording");
                    dbusTrayIcon.SetTooltip("Push To Talk - Recording...");
                    break;
                case DictationState.Transcribing:
                    // Change icon back to white immediately when recording stops (issue #28)
                    dbusTrayIcon.SetIcon("trigger-ptt");
                    dbusTrayIcon.SetTooltip("Push To Talk - Transcribing...");
                    // Show animated icon NEXT TO main icon (main stays visible)
                    await animatedIcon.ShowAsync();
                    break;
            }

            // Broadcast state change to SignalR clients
            var eventType = state switch
            {
                DictationState.Recording => DictationEventType.RecordingStarted,
                DictationState.Transcribing => DictationEventType.TranscriptionStarted,
                _ => DictationEventType.RecordingStopped
            };

            await hubContext.Clients.All.SendAsync("DictationEvent", new DictationEvent
            {
                EventType = eventType,
                Text = null
            });
        };

        // Handle transcription completion - send result to SignalR clients
        dictationService.TranscriptionCompleted += async (_, text) =>
        {
            await hubContext.Clients.All.SendAsync("DictationEvent", new DictationEvent
            {
                EventType = DictationEventType.TranscriptionCompleted,
                Text = text
            });
        };

        // Handle click on tray icon
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
            AboutDialog.Show(version);
        };
    }
    else
    {
        logger.LogWarning("D-Bus tray icon failed to initialize, continuing without tray icon");
    }

    // Start web server in background
    Task.Run(async () =>
    {
        try
        {
            await webApp.StartAsync(cts.Token);
            // Wait until cancellation is requested
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }).FireAndForget(logger, "WebServer");

    Console.WriteLine($"Web server started on http://localhost:{webPort}");
    Console.WriteLine($"Remote control: http://localhost:{webPort}/remote.html");

    // Start keyboard monitoring in background
    Task.Run(async () =>
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
    }).FireAndForget(logger, "KeyboardMonitoring");

    var triggerKey = options.GetTriggerKeyCode();
    Console.WriteLine($"Keyboard monitoring started ({triggerKey} to trigger)");
    Console.WriteLine("Press Ctrl+C to exit");
    Console.WriteLine();

    // Handle Ctrl+C (SIGINT)
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Console.WriteLine("\nCtrl+C pressed - shutting down...");
        cts.Cancel();
    };

    // Handle SIGTERM (systemd shutdown, GNOME logout, etc.)
    using var sigtermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
    {
        Console.WriteLine("\nSIGTERM received - shutting down...");
        context.Cancel = true; // Prevent default termination, let us clean up
        cts.Cancel();
    });

    // Keep the application running
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
    await webApp.DisposeAsync();
    dictationService.Dispose();
    animatedIcon.Dispose();
    dbusTrayIcon.Dispose();
}

Console.WriteLine("PushToTalk stopped");
