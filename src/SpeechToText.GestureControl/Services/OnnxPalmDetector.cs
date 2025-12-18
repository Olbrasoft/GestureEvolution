using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using Olbrasoft.SpeechToText.GestureControl.Models;

namespace Olbrasoft.SpeechToText.GestureControl.Services;

/// <summary>
/// Palm detector using ONNX model from MediaPipe.
/// Pure C# implementation without Python dependency.
/// </summary>
public class OnnxPalmDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string[] _inputNames;
    private readonly string[] _outputNames;
    private readonly float _scoreThreshold;
    private bool _disposed;

    // Model expects 192x192 input
    private const int InputWidth = 192;
    private const int InputHeight = 192;

    public OnnxPalmDetector(string modelPath, float scoreThreshold = 0.60f, bool useCuda = true)
    {
        var options = new SessionOptions();
        options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING;

        if (useCuda)
        {
            try
            {
                options.AppendExecutionProvider_CUDA(0);
                Console.WriteLine("Palm detector: Using CUDA GPU");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Palm detector: CUDA not available ({ex.Message}), using CPU");
            }
        }

        _session = new InferenceSession(modelPath, options);
        _scoreThreshold = scoreThreshold;

        _inputNames = _session.InputMetadata.Keys.ToArray();
        _outputNames = _session.OutputMetadata.Keys.ToArray();

        Console.WriteLine($"Palm detector loaded: {modelPath}");
        Console.WriteLine($"  Inputs: {string.Join(", ", _inputNames)}");
        Console.WriteLine($"  Outputs: {string.Join(", ", _outputNames)}");
    }

    /// <summary>
    /// Detect palms in the image.
    /// </summary>
    /// <param name="jpegData">JPEG image data</param>
    /// <returns>List of detected hands with normalized positions</returns>
    public List<HandInfo> Detect(byte[] jpegData)
    {
        using var bitmap = SKBitmap.Decode(jpegData);
        if (bitmap == null)
            return new List<HandInfo>();

        var imageWidth = bitmap.Width;
        var imageHeight = bitmap.Height;

        // Preprocess: resize to 192x192, normalize to 0-1, convert to CHW RGB
        var inputTensor = Preprocess(bitmap);

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputNames[0], inputTensor)
        };

        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();

        // Postprocess: extract hand positions
        return Postprocess(output, imageWidth, imageHeight);
    }

    private DenseTensor<float> Preprocess(SKBitmap bitmap)
    {
        // Resize to model input size
        using var resized = bitmap.Resize(new SKImageInfo(InputWidth, InputHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

        // Create tensor [1, 3, 192, 192] in CHW format
        var tensor = new DenseTensor<float>(new[] { 1, 3, InputHeight, InputWidth });

        for (int y = 0; y < InputHeight; y++)
        {
            for (int x = 0; x < InputWidth; x++)
            {
                var pixel = resized.GetPixel(x, y);

                // Normalize to 0-1 and convert BGR->RGB
                tensor[0, 0, y, x] = pixel.Red / 255f;   // R
                tensor[0, 1, y, x] = pixel.Green / 255f; // G
                tensor[0, 2, y, x] = pixel.Blue / 255f;  // B
            }
        }

        return tensor;
    }

    private List<HandInfo> Postprocess(Tensor<float> output, int imageWidth, int imageHeight)
    {
        var hands = new List<HandInfo>();

        // Output shape: [N, 8] where each row is:
        // pd_score, box_x, box_y, box_size, kp0_x, kp0_y, kp2_x, kp2_y
        var outputArray = output.ToArray();
        int numDetections = output.Dimensions[0];
        int stride = 8;

        for (int i = 0; i < numDetections; i++)
        {
            int offset = i * stride;
            float pdScore = outputArray[offset + 0];

            if (pdScore < _scoreThreshold)
                continue;

            float boxX = outputArray[offset + 1];
            float boxY = outputArray[offset + 2];
            float boxSize = outputArray[offset + 3];
            float kp0X = outputArray[offset + 4];
            float kp0Y = outputArray[offset + 5];
            float kp2X = outputArray[offset + 6];
            float kp2Y = outputArray[offset + 7];

            if (boxSize <= 0)
                continue;

            // Calculate rotation from keypoints (wrist to middle finger MCP)
            float kp02X = kp2X - kp0X;
            float kp02Y = kp2Y - kp0Y;
            float rotation = 0.5f * MathF.PI - MathF.Atan2(-kp02Y, kp02X);

            // Calculate center position
            float centerX = boxX + 0.5f * boxSize * MathF.Sin(rotation);
            float centerY = boxY - 0.5f * boxSize * MathF.Cos(rotation);

            // Determine left/right based on position (simple heuristic)
            // In a mirrored image, left side = right hand
            var handType = centerX < 0.5f ? HandType.Right : HandType.Left;

            hands.Add(new HandInfo
            {
                HandType = handType,
                NormalizedX = centerX,
                NormalizedY = centerY,
                BoxSize = boxSize
            });
        }

        return hands;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }
}
