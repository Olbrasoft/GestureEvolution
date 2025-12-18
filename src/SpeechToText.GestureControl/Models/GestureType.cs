namespace Olbrasoft.SpeechToText.GestureControl.Models;

/// <summary>
/// Types of hand gestures that can be detected.
/// </summary>
public enum GestureType
{
    None,
    Victory,        // 2 fingers (peace sign)
    OpenPalm,       // 5 fingers extended
    Fist,           // 0 fingers (closed fist)
    ThumbsUp,       // Thumb extended
    ThumbsDown,     // Thumb down
    PointingUp,     // Index finger pointing
    Ok,             // Thumb and index forming circle
    ILoveYou,       // Thumb + index + pinky
    SwipeLeft,      // Horizontal movement left
    SwipeRight,     // Horizontal movement right
    SwipeUp,        // Vertical movement up
    SwipeDown       // Vertical movement down
}
