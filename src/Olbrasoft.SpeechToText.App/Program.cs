using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.App;

// Single instance check
using var instanceLock = SingleInstanceLock.TryAcquire();
if (!instanceLock.IsAcquired)
{
    Console.WriteLine("ERROR: SpeechToText is already running!");
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
Console.WriteLine("║              SpeechToText Desktop Application                ║");
Console.WriteLine($"║                      Version: {version,-25}       ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();
logger.LogInformation("SpeechToText version {Version} starting", version);

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

// Build services
var services = new ServiceCollection();
services.AddSingleton(loggerFactory);
services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
services.AddDictationServices(options);
services.AddTrayServices(options, iconsPath);

using var serviceProvider = services.BuildServiceProvider();

// Get services
var dictationService = serviceProvider.GetRequiredService<DictationService>();
var dbusTrayIcon = serviceProvider.GetRequiredService<DBusTrayIcon>();
var animatedIcon = serviceProvider.GetRequiredService<DBusAnimatedIcon>();

logger.LogInformation("Whisper model loaded: {Path}", modelPath);
logger.LogInformation("Text typer: {DisplayServer}", Olbrasoft.SpeechToText.TextInput.TextTyperFactory.GetDisplayServerName());

var cts = new CancellationTokenSource();

try
{
    // Initialize D-Bus tray icons
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

    var triggerKey = options.GetTriggerKeyCode();
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
    dictationService.Dispose();
    animatedIcon.Dispose();
    dbusTrayIcon.Dispose();
}

Console.WriteLine("SpeechToText stopped");
