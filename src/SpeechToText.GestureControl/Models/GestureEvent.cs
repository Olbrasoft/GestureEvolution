namespace Olbrasoft.SpeechToText.GestureControl.Models;

/// <summary>
/// Event data for gesture detection.
/// </summary>
public record GestureEvent
{
    public GestureType GestureType { get; init; }
    public bool IsLeftHand { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public float Confidence { get; init; }
    public int ExtendedFingers { get; init; }
}
