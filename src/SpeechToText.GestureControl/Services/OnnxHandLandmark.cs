using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Olbrasoft.SpeechToText.GestureControl.Services;

/// <summary>
/// Hand landmark detection using ONNX model.
/// Detects 21 landmarks on hand and determines if palm is open.
/// </summary>
public class OnnxHandLandmark : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string[] _outputNames;
    private readonly float _scoreThreshold;

    private const int InputWidth = 224;
    private const int InputHeight = 224;

    // MediaPipe hand landmark indices
    public static class Landmark
    {
        public const int Wrist = 0;
        public const int ThumbCmc = 1;
        public const int ThumbMcp = 2;
        public const int ThumbIp = 3;
        public const int ThumbTip = 4;
        public const int IndexMcp = 5;
        public const int IndexPip = 6;
        public const int IndexDip = 7;
        public const int IndexTip = 8;
        public const int MiddleMcp = 9;
        public const int MiddlePip = 10;
        public const int MiddleDip = 11;
        public const int MiddleTip = 12;
        public const int RingMcp = 13;
        public const int RingPip = 14;
        public const int RingDip = 15;
        public const int RingTip = 16;
        public const int PinkyMcp = 17;
        public const int PinkyPip = 18;
        public const int PinkyDip = 19;
        public const int PinkyTip = 20;
    }

    public OnnxHandLandmark(string modelPath, float scoreThreshold = 0.5f, bool useCuda = true)
    {
        _scoreThreshold = scoreThreshold;

        var sessionOptions = new SessionOptions();
        sessionOptions.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING;

        if (useCuda)
        {
            try
            {
                sessionOptions.AppendExecutionProvider_CUDA(0);
                Console.WriteLine("Hand landmark: Using CUDA GPU");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Hand landmark: CUDA not available ({ex.Message}), using CPU");
            }
        }

        _session = new InferenceSession(modelPath, sessionOptions);

        _inputName = _session.InputMetadata.Keys.First();
        _outputNames = _session.OutputMetadata.Keys.ToArray();

        Console.WriteLine($"Hand landmark model loaded: {modelPath}");
        Console.WriteLine($"  Input: {_inputName}");
        Console.WriteLine($"  Outputs: {string.Join(", ", _outputNames)}");
    }

    /// <summary>
    /// Detect hand landmarks from a cropped palm image.
    /// </summary>
    /// <param name="palmImage">Cropped image of the palm region</param>
    /// <returns>Hand landmark result or null if not detected</returns>
    public HandLandmarkResult? Detect(SKBitmap palmImage)
    {
        // Preprocess: resize to 224x224, normalize, convert to CHW
        var inputTensor = Preprocess(palmImage);

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
        };

        using var results = _session.Run(inputs);

        // Parse outputs
        var xyzTensor = results.First(r => r.Name == "xyz_x21").AsTensor<float>();
        var scoreTensor = results.First(r => r.Name == "hand_score").AsTensor<float>();
        var handTypeTensor = results.First(r => r.Name == "lefthand_0_or_righthand_1").AsTensor<float>();

        var score = scoreTensor[0, 0];
        if (score < _scoreThreshold)
            return null;

        // Parse 21 landmarks (63 values = 21 * 3 for x,y,z)
        var landmarks = new Point3D[21];
        for (int i = 0; i < 21; i++)
        {
            landmarks[i] = new Point3D(
                xyzTensor[0, i * 3 + 0] / InputWidth,      // Normalize to 0-1
                xyzTensor[0, i * 3 + 1] / InputHeight,
                xyzTensor[0, i * 3 + 2] / InputWidth       // Z is also normalized
            );
        }

        var handTypeScore = handTypeTensor[0, 0];
        var isLeftHand = handTypeScore < 0.5f;
        var extendedFingers = CountExtendedFingers(landmarks, isLeftHand);
        var isOpenPalm = extendedFingers >= 4;  // 4 or 5 fingers = open palm ready for swipe

        // Debug: show what model reports
        // Console.WriteLine($"[DEBUG] HandType score={handTypeScore:F2}, isLeft={isLeftHand}");

        return new HandLandmarkResult
        {
            Landmarks = landmarks,
            Score = score,
            IsLeftHand = isLeftHand,
            IsOpenPalm = isOpenPalm,
            ExtendedFingers = extendedFingers
        };
    }

    /// <summary>
    /// Detect hand landmarks from palm detection result.
    /// </summary>
    public HandLandmarkResult? Detect(byte[] jpegData, float palmCenterX, float palmCenterY, float palmSize)
    {
        using var originalBitmap = SKBitmap.Decode(jpegData);
        if (originalBitmap == null)
            return null;

        // Crop palm region with some padding
        var cropSize = (int)(palmSize * originalBitmap.Width * 1.5f);  // Add 50% padding
        var cropX = (int)(palmCenterX * originalBitmap.Width - cropSize / 2);
        var cropY = (int)(palmCenterY * originalBitmap.Height - cropSize / 2);

        // Clamp to image bounds
        cropX = Math.Max(0, cropX);
        cropY = Math.Max(0, cropY);
        cropSize = Math.Min(cropSize, originalBitmap.Width - cropX);
        cropSize = Math.Min(cropSize, originalBitmap.Height - cropY);

        if (cropSize < 50)  // Too small
            return null;

        // Extract crop
        var cropRect = new SKRectI(cropX, cropY, cropX + cropSize, cropY + cropSize);
        using var croppedBitmap = new SKBitmap(cropSize, cropSize);
        using (var canvas = new SKCanvas(croppedBitmap))
        {
            canvas.DrawBitmap(originalBitmap, cropRect, new SKRect(0, 0, cropSize, cropSize));
        }

        var result = Detect(croppedBitmap);

        // Transform landmarks back to original image coordinates
        if (result != null)
        {
            for (int i = 0; i < result.Landmarks.Length; i++)
            {
                var lm = result.Landmarks[i];
                result.Landmarks[i] = new Point3D(
                    (cropX + lm.X * cropSize) / originalBitmap.Width,
                    (cropY + lm.Y * cropSize) / originalBitmap.Height,
                    lm.Z
                );
            }
        }

        return result;
    }

    private DenseTensor<float> Preprocess(SKBitmap bitmap)
    {
        // Resize to 224x224
        using var resized = bitmap.Resize(new SKImageInfo(InputWidth, InputHeight), SKSamplingOptions.Default);

        // Create tensor [1, 3, 224, 224] in CHW format, normalized to 0-1
        var tensor = new DenseTensor<float>(new[] { 1, 3, InputHeight, InputWidth });

        for (int y = 0; y < InputHeight; y++)
        {
            for (int x = 0; x < InputWidth; x++)
            {
                var pixel = resized.GetPixel(x, y);
                // RGB channels, normalized to 0-1
                tensor[0, 0, y, x] = pixel.Red / 255f;
                tensor[0, 1, y, x] = pixel.Green / 255f;
                tensor[0, 2, y, x] = pixel.Blue / 255f;
            }
        }

        return tensor;
    }

    /// <summary>
    /// Count how many fingers are extended.
    /// Uses Y-coordinate comparison like MediaPipe: finger is extended if tip.Y &lt; PIP.Y
    /// (In image coordinates, Y increases downward, so lower Y = higher position = extended)
    /// </summary>
    private int CountExtendedFingers(Point3D[] landmarks, bool isLeftHand)
    {
        int count = 0;

        // Thumb - special case: compare X coordinates relative to hand orientation
        // In mirrored camera view:
        // - Left hand appears on right side, thumb extends to the right (larger X)
        // - Right hand appears on left side, thumb extends to the left (smaller X)
        var thumbTip = landmarks[Landmark.ThumbTip];
        var thumbIp = landmarks[Landmark.ThumbIp];

        // For thumb, check if tip is extended outward from IP joint
        // From debug: thumb folded ~0.06-0.08, thumb extended ~0.10-0.13
        // Using 0.10 threshold - only clearly extended thumb counts
        var thumbDiff = MathF.Abs(thumbTip.X - thumbIp.X);
        if (thumbDiff > 0.10f)
            count++;

        // Index finger - tip.Y < PIP.Y means extended (lower Y = higher position)
        if (IsFingerExtendedByY(landmarks, Landmark.IndexTip, Landmark.IndexPip, Landmark.IndexMcp))
            count++;

        // Middle finger
        if (IsFingerExtendedByY(landmarks, Landmark.MiddleTip, Landmark.MiddlePip, Landmark.MiddleMcp))
            count++;

        // Ring finger
        if (IsFingerExtendedByY(landmarks, Landmark.RingTip, Landmark.RingPip, Landmark.RingMcp))
            count++;

        // Pinky
        if (IsFingerExtendedByY(landmarks, Landmark.PinkyTip, Landmark.PinkyPip, Landmark.PinkyMcp))
            count++;

        return count;
    }

    /// <summary>
    /// Check if a finger is extended using Y-coordinate comparison.
    /// Finger is extended if: tip.Y &lt; PIP.Y (tip is above PIP in image coords)
    /// Matches Python/MediaPipe logic exactly - no tolerance needed.
    /// </summary>
    private bool IsFingerExtendedByY(Point3D[] landmarks, int tipIndex, int pipIndex, int mcpIndex)
    {
        var tip = landmarks[tipIndex];
        var pip = landmarks[pipIndex];

        // Finger is extended if tip is above PIP (lower Y value)
        // Same as Python: tip.y < pip.y
        return tip.Y < pip.Y;
    }

    private float Distance2D(Point3D a, Point3D b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}

/// <summary>
/// 3D point with X, Y, Z coordinates (normalized 0-1).
/// </summary>
public record struct Point3D(float X, float Y, float Z);

/// <summary>
/// Result of hand landmark detection.
/// </summary>
public class HandLandmarkResult
{
    /// <summary>
    /// 21 hand landmarks in normalized coordinates (0-1).
    /// </summary>
    public required Point3D[] Landmarks { get; set; }

    /// <summary>
    /// Detection confidence score.
    /// </summary>
    public float Score { get; set; }

    /// <summary>
    /// True if this is a left hand, false if right hand.
    /// </summary>
    public bool IsLeftHand { get; set; }

    /// <summary>
    /// True if all 5 fingers are extended (open palm).
    /// </summary>
    public bool IsOpenPalm { get; set; }

    /// <summary>
    /// Number of extended fingers (0-5).
    /// </summary>
    public int ExtendedFingers { get; set; }

    /// <summary>
    /// Get the wrist position (landmark 0).
    /// </summary>
    public Point3D Wrist => Landmarks[OnnxHandLandmark.Landmark.Wrist];

    /// <summary>
    /// Get the middle finger MCP (landmark 9) - center of palm.
    /// </summary>
    public Point3D PalmCenter => Landmarks[OnnxHandLandmark.Landmark.MiddleMcp];

    /// <summary>
    /// Check if this is a pointing gesture (index finger extended, others curled).
    /// Uses finger LENGTH comparison - works for horizontal pointing too.
    /// </summary>
    public bool IsPointing
    {
        get
        {
            // Index finger landmarks
            var indexTip = Landmarks[OnnxHandLandmark.Landmark.IndexTip];
            var indexMcp = Landmarks[OnnxHandLandmark.Landmark.IndexMcp];

            // Calculate index finger length
            var indexLength = MathF.Sqrt(
                MathF.Pow(indexTip.X - indexMcp.X, 2) +
                MathF.Pow(indexTip.Y - indexMcp.Y, 2));

            // Calculate other finger lengths
            var middleTip = Landmarks[OnnxHandLandmark.Landmark.MiddleTip];
            var middleMcp = Landmarks[OnnxHandLandmark.Landmark.MiddleMcp];
            var middleLength = MathF.Sqrt(
                MathF.Pow(middleTip.X - middleMcp.X, 2) +
                MathF.Pow(middleTip.Y - middleMcp.Y, 2));

            var ringTip = Landmarks[OnnxHandLandmark.Landmark.RingTip];
            var ringMcp = Landmarks[OnnxHandLandmark.Landmark.RingMcp];
            var ringLength = MathF.Sqrt(
                MathF.Pow(ringTip.X - ringMcp.X, 2) +
                MathF.Pow(ringTip.Y - ringMcp.Y, 2));

            var pinkyTip = Landmarks[OnnxHandLandmark.Landmark.PinkyTip];
            var pinkyMcp = Landmarks[OnnxHandLandmark.Landmark.PinkyMcp];
            var pinkyLength = MathF.Sqrt(
                MathF.Pow(pinkyTip.X - pinkyMcp.X, 2) +
                MathF.Pow(pinkyTip.Y - pinkyMcp.Y, 2));

            // Index should be significantly longer than the AVERAGE of other fingers
            // This is more forgiving than requiring longer than EACH finger
            var avgOtherLength = (middleLength + ringLength + pinkyLength) / 3f;
            var indexLongerThanAvg = indexLength > avgOtherLength * 1.4f;  // 40% longer than average

            // Also: index must be longer than middle (the longest other finger)
            var indexLongerThanMiddle = indexLength > middleLength * 1.0f;

            return indexLongerThanAvg && indexLongerThanMiddle;
        }
    }

    /// <summary>
    /// Get pointing direction as X difference between index tip and MCP.
    /// Simple and reliable: tip.X - mcp.X
    /// Positive = right, Negative = left.
    /// </summary>
    public float? PointingDirection
    {
        get
        {
            if (!IsPointing) return null;

            var indexMcp = Landmarks[OnnxHandLandmark.Landmark.IndexMcp];
            var indexTip = Landmarks[OnnxHandLandmark.Landmark.IndexTip];

            // Simple X difference: tip.X - mcp.X
            return indexTip.X - indexMcp.X;
        }
    }

    /// <summary>
    /// Check if pointing left (from USER's perspective).
    /// In mirrored camera view, pointing left = negative X direction.
    /// Same logic for both hands since image is already flipped.
    /// </summary>
    public bool IsPointingLeft
    {
        get
        {
            var dir = PointingDirection;
            if (!dir.HasValue) return false;

            // In mirrored view: pointing left = tip.X < mcp.X = negative dx
            return dir.Value < -0.02f;
        }
    }

    /// <summary>
    /// Check if pointing right (from USER's perspective).
    /// In mirrored camera view, pointing right = positive X direction.
    /// Same logic for both hands since image is already flipped.
    /// </summary>
    public bool IsPointingRight
    {
        get
        {
            var dir = PointingDirection;
            if (!dir.HasValue) return false;

            // In mirrored view: pointing right = tip.X > mcp.X = positive dx
            return dir.Value > 0.02f;
        }
    }

    /// <summary>
    /// Check if this is a closed fist (all fingers curled).
    /// </summary>
    public bool IsClosedFist
    {
        get
        {
            // All fingers must be curled (tip.Y >= pip.Y means finger is bent down)
            var indexTip = Landmarks[OnnxHandLandmark.Landmark.IndexTip];
            var indexPip = Landmarks[OnnxHandLandmark.Landmark.IndexPip];
            var indexCurled = indexTip.Y >= indexPip.Y;

            var middleTip = Landmarks[OnnxHandLandmark.Landmark.MiddleTip];
            var middlePip = Landmarks[OnnxHandLandmark.Landmark.MiddlePip];
            var middleCurled = middleTip.Y >= middlePip.Y;

            var ringTip = Landmarks[OnnxHandLandmark.Landmark.RingTip];
            var ringPip = Landmarks[OnnxHandLandmark.Landmark.RingPip];
            var ringCurled = ringTip.Y >= ringPip.Y;

            var pinkyTip = Landmarks[OnnxHandLandmark.Landmark.PinkyTip];
            var pinkyPip = Landmarks[OnnxHandLandmark.Landmark.PinkyPip];
            var pinkyCurled = pinkyTip.Y >= pinkyPip.Y;

            // Thumb should also be curled (close to palm)
            var thumbTip = Landmarks[OnnxHandLandmark.Landmark.ThumbTip];
            var thumbIp = Landmarks[OnnxHandLandmark.Landmark.ThumbIp];
            var thumbCurled = MathF.Abs(thumbTip.X - thumbIp.X) < 0.08f;

            return indexCurled && middleCurled && ringCurled && pinkyCurled && thumbCurled;
        }
    }

    /// <summary>
    /// Check if this is the OK gesture (thumb tip touching index finger tip).
    /// </summary>
    public bool IsOkGesture
    {
        get
        {
            var thumbTip = Landmarks[OnnxHandLandmark.Landmark.ThumbTip];
            var indexTip = Landmarks[OnnxHandLandmark.Landmark.IndexTip];

            // Calculate distance between thumb tip and index tip
            var dx = thumbTip.X - indexTip.X;
            var dy = thumbTip.Y - indexTip.Y;
            var distance = MathF.Sqrt(dx * dx + dy * dy);

            // OK gesture if tips are close (stricter threshold ~0.05 in normalized coords)
            return distance < 0.05f;
        }
    }
}
