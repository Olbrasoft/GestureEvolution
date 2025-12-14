using Olbrasoft.SpeechToText;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// Strongly-typed configuration options for the dictation application.
/// </summary>
public class DictationOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Dictation";

    /// <summary>
    /// Path to the keyboard device (null for auto-detect).
    /// </summary>
    public string? KeyboardDevice { get; set; }

    /// <summary>
    /// Path to the Whisper GGML model file.
    /// </summary>
    public string GgmlModelPath { get; set; } = "models/ggml-medium.bin";

    /// <summary>
    /// Whisper language code (e.g., "cs", "en").
    /// </summary>
    public string WhisperLanguage { get; set; } = "cs";

    /// <summary>
    /// Key that triggers dictation (e.g., "CapsLock").
    /// </summary>
    public string TriggerKey { get; set; } = "CapsLock";

    /// <summary>
    /// Key that cancels transcription (e.g., "Escape").
    /// </summary>
    public string CancelKey { get; set; } = "Escape";

    /// <summary>
    /// Path to the sound file played during transcription.
    /// </summary>
    public string? TranscriptionSoundPath { get; set; }

    /// <summary>
    /// Whether to show animated icon during transcription.
    /// </summary>
    public bool ShowTranscriptionAnimation { get; set; } = true;

    /// <summary>
    /// Path to text filters file for removing Whisper hallucinations.
    /// </summary>
    public string? TextFiltersPath { get; set; }

    /// <summary>
    /// Path to the icons directory.
    /// </summary>
    public string? IconsPath { get; set; }

    /// <summary>
    /// Icon size in pixels.
    /// </summary>
    public int IconSize { get; set; } = 22;

    /// <summary>
    /// Animation interval in milliseconds.
    /// </summary>
    public int AnimationIntervalMs { get; set; } = 150;

    /// <summary>
    /// Animation frame names (without extension).
    /// </summary>
    public string[] AnimationFrames { get; set; } =
    [
        "document-white-frame1",
        "document-white-frame2",
        "document-white-frame3",
        "document-white-frame4",
        "document-white-frame5"
    ];

    /// <summary>
    /// Parses the TriggerKey string to KeyCode enum.
    /// </summary>
    public KeyCode GetTriggerKeyCode()
    {
        return Enum.TryParse<KeyCode>(TriggerKey, ignoreCase: true, out var key)
            ? key
            : KeyCode.CapsLock;
    }

    /// <summary>
    /// Parses the CancelKey string to KeyCode enum.
    /// </summary>
    public KeyCode GetCancelKeyCode()
    {
        return Enum.TryParse<KeyCode>(CancelKey, ignoreCase: true, out var key)
            ? key
            : KeyCode.Escape;
    }

    /// <summary>
    /// Gets the full path for GgmlModelPath, resolving relative paths.
    /// </summary>
    public string GetFullGgmlModelPath()
    {
        return Path.IsPathRooted(GgmlModelPath)
            ? GgmlModelPath
            : Path.Combine(AppContext.BaseDirectory, GgmlModelPath);
    }

    /// <summary>
    /// Gets the full path for TranscriptionSoundPath, resolving relative paths.
    /// </summary>
    public string? GetFullTranscriptionSoundPath()
    {
        if (string.IsNullOrWhiteSpace(TranscriptionSoundPath))
            return null;

        return Path.IsPathRooted(TranscriptionSoundPath)
            ? TranscriptionSoundPath
            : Path.Combine(AppContext.BaseDirectory, TranscriptionSoundPath);
    }

    /// <summary>
    /// Gets the full path for TextFiltersPath, resolving relative paths.
    /// </summary>
    public string? GetFullTextFiltersPath()
    {
        if (string.IsNullOrWhiteSpace(TextFiltersPath))
            return null;

        return Path.IsPathRooted(TextFiltersPath)
            ? TextFiltersPath
            : Path.Combine(AppContext.BaseDirectory, TextFiltersPath);
    }
}
