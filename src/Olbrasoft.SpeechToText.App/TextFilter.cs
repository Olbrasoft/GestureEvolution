using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// Configuration for text filters loaded from JSON file.
/// </summary>
public class TextFiltersConfig
{
    /// <summary>
    /// List of text patterns to remove from transcription output.
    /// </summary>
    public List<string> Remove { get; set; } = new();
}

/// <summary>
/// Filters unwanted text patterns from Whisper transcription output.
/// </summary>
public class TextFilter
{
    private readonly ILogger<TextFilter> _logger;
    private readonly string? _configPath;
    private List<string> _patterns = new();
    private DateTime _lastModified;
    private readonly object _lock = new();

    public TextFilter(ILogger<TextFilter> logger, string? configPath = null)
    {
        _logger = logger;
        _configPath = configPath;

        if (!string.IsNullOrWhiteSpace(_configPath))
        {
            LoadFilters();
        }
        else
        {
            _logger.LogInformation("Text filtering disabled (no config path)");
        }
    }

    /// <summary>
    /// Gets whether filtering is enabled.
    /// </summary>
    public bool IsEnabled => _patterns.Count > 0;

    /// <summary>
    /// Gets the number of loaded filter patterns.
    /// </summary>
    public int PatternCount => _patterns.Count;

    /// <summary>
    /// Applies all filters to the input text.
    /// </summary>
    /// <param name="text">Text to filter.</param>
    /// <returns>Filtered text with patterns removed.</returns>
    public string Apply(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Check for file changes (hot reload)
        CheckForUpdates();

        var result = text;
        var originalLength = result.Length;

        lock (_lock)
        {
            foreach (var pattern in _patterns)
            {
                if (result.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    var before = result;
                    result = result.Replace(pattern, "", StringComparison.OrdinalIgnoreCase);
                    _logger.LogDebug("Filtered pattern '{Pattern}' from text", pattern);
                }
            }
        }

        // Trim whitespace after filtering
        result = result.Trim();

        // Normalize multiple spaces to single space
        while (result.Contains("  "))
        {
            result = result.Replace("  ", " ");
        }

        if (result.Length != originalLength)
        {
            _logger.LogDebug("Text filtered: {Original} chars -> {Result} chars", originalLength, result.Length);
        }

        return result;
    }

    /// <summary>
    /// Reloads filters from the configuration file.
    /// </summary>
    public void Reload()
    {
        LoadFilters();
    }

    private void LoadFilters()
    {
        if (string.IsNullOrWhiteSpace(_configPath))
            return;

        try
        {
            if (!File.Exists(_configPath))
            {
                _logger.LogWarning("Text filters config not found: {Path}", _configPath);
                return;
            }

            var fileInfo = new FileInfo(_configPath);

            lock (_lock)
            {
                _lastModified = fileInfo.LastWriteTimeUtc;

                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<TextFiltersConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config?.Remove != null)
                {
                    _patterns = config.Remove
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .ToList();

                    _logger.LogInformation("Loaded {Count} text filter patterns from {Path}",
                        _patterns.Count, _configPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load text filters from {Path}", _configPath);
        }
    }

    private void CheckForUpdates()
    {
        if (string.IsNullOrWhiteSpace(_configPath) || !File.Exists(_configPath))
            return;

        try
        {
            var fileInfo = new FileInfo(_configPath);
            if (fileInfo.LastWriteTimeUtc > _lastModified)
            {
                _logger.LogInformation("Text filters config changed, reloading...");
                LoadFilters();
            }
        }
        catch
        {
            // Ignore file access errors during check
        }
    }
}
