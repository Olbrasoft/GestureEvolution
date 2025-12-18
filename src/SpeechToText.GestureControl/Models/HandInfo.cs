namespace Olbrasoft.SpeechToText.GestureControl.Models;

/// <summary>
/// Information about a detected hand from palm detection.
/// </summary>
public class HandInfo
{
    /// <summary>
    /// Type of detected hand (left or right).
    /// </summary>
    public HandType HandType { get; init; }

    /// <summary>
    /// Normalized X position of hand center (0-1).
    /// </summary>
    public float NormalizedX { get; init; }

    /// <summary>
    /// Normalized Y position of hand center (0-1).
    /// </summary>
    public float NormalizedY { get; init; }

    /// <summary>
    /// Size of bounding box in normalized coordinates.
    /// </summary>
    public float BoxSize { get; init; }
}
