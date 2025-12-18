using FlashCap;

namespace Olbrasoft.SpeechToText.GestureControl.Services;

/// <summary>
/// Service for capturing frames from camera using FlashCap (V4L2).
/// Optimized for Logitech StreamCam.
/// </summary>
public class CameraService : IAsyncDisposable
{
    private readonly CaptureDevice _device;
    private readonly FrameBuffer _frameBuffer;
    private bool _disposed;

    public int FrameWidth { get; }
    public int FrameHeight { get; }

    private CameraService(CaptureDevice device, FrameBuffer frameBuffer, int width, int height)
    {
        _device = device;
        _frameBuffer = frameBuffer;
        FrameWidth = width;
        FrameHeight = height;
    }

    public static async Task<CameraService> CreateAsync(int cameraIndex = 0, int width = 960, int height = 540, CancellationToken ct = default)
    {
        var devices = new CaptureDevices();
        var descriptors = devices.EnumerateDescriptors().ToList();

        if (descriptors.Count == 0)
        {
            throw new InvalidOperationException("No capture devices found");
        }

        Console.WriteLine($"Found {descriptors.Count} capture device(s):");
        for (int i = 0; i < descriptors.Count; i++)
        {
            var desc = descriptors[i];
            var charCount = desc.Characteristics.Count();
            Console.WriteLine($"  [{i}] {desc.Name} ({desc.Identity}) - {charCount} modes");
        }

        if (cameraIndex >= descriptors.Count)
        {
            throw new ArgumentException($"Camera index {cameraIndex} not found. Available: 0-{descriptors.Count - 1}");
        }

        // Find first descriptor with available characteristics (skip metadata devices)
        var descriptor = descriptors
            .Skip(cameraIndex)
            .FirstOrDefault(d => d.Characteristics.Any());

        if (descriptor == null)
        {
            // Try from beginning
            descriptor = descriptors.FirstOrDefault(d => d.Characteristics.Any());
        }

        if (descriptor == null)
        {
            throw new InvalidOperationException("No camera with available video modes found");
        }
        Console.WriteLine($"\nUsing camera: {descriptor.Name}");

        // List all available characteristics
        var allChars = descriptor.Characteristics.ToList();
        Console.WriteLine($"Available video modes ({allChars.Count}):");
        foreach (var c in allChars.Take(10))
        {
            Console.WriteLine($"  {c.Width}x{c.Height} {c.PixelFormat}");
        }
        if (allChars.Count > 10)
        {
            Console.WriteLine($"  ... and {allChars.Count - 10} more");
        }

        // Debug: print actual PixelFormat values
        Console.WriteLine("\nPixelFormat debug (first 5):");
        foreach (var c in allChars.Take(5))
        {
            var pf = c.PixelFormat.ToString();
            Console.WriteLine($"  {c.Width}x{c.Height} PixelFormat='{pf}'");
        }

        // Find best matching characteristics - MUST use JPEG/MJPEG for proper image output
        var jpegChars = allChars
            .Where(c =>
            {
                var pf = c.PixelFormat.ToString();
                return pf.Contains("Jpeg", StringComparison.OrdinalIgnoreCase) ||
                       pf.Contains("MJPG", StringComparison.OrdinalIgnoreCase) ||
                       pf.Contains("MJPEG", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        Console.WriteLine($"\nJPEG modes available: {jpegChars.Count}");

        var characteristics = jpegChars
            .Where(c => c.Width == width && c.Height == height)
            .FirstOrDefault();

        // Fallback to closest JPEG format
        if (characteristics == null)
        {
            characteristics = jpegChars
                .OrderBy(c => Math.Abs(c.Width - width) + Math.Abs(c.Height - height))
                .FirstOrDefault();
        }

        // If no JPEG at all, throw error (YUYV won't work with our pipeline)
        if (characteristics == null)
        {
            throw new InvalidOperationException("No JPEG/MJPEG video mode found. Camera must support JPEG format.");
        }

        Console.WriteLine($"\nSelected: {characteristics.Width}x{characteristics.Height} {characteristics.PixelFormat}");

        // Create shared frame buffer
        var frameBuffer = new FrameBuffer();

        // Open device with callback that writes to shared buffer
        var device = await descriptor.OpenAsync(characteristics, frameBuffer.OnFrameArrived, ct);
        await device.StartAsync(ct);

        Console.WriteLine("Camera started");
        return new CameraService(device, frameBuffer, characteristics.Width, characteristics.Height);
    }

    /// <summary>
    /// Get the current frame as raw bytes.
    /// </summary>
    public byte[]? GetCurrentFrame()
    {
        return _frameBuffer.GetCurrentFrame();
    }

    /// <summary>
    /// Wait for a new frame to be available.
    /// </summary>
    public async Task<byte[]?> WaitForFrameAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < timeout)
        {
            var frame = GetCurrentFrame();
            if (frame != null)
            {
                return frame;
            }
            await Task.Delay(10, ct);
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _device.StopAsync();
        await _device.DisposeAsync();
    }

    /// <summary>
    /// Shared frame buffer for callback.
    /// </summary>
    private class FrameBuffer
    {
        private byte[]? _currentFrame;
        private readonly object _frameLock = new();

        public void OnFrameArrived(PixelBufferScope bufferScope)
        {
            try
            {
                lock (_frameLock)
                {
                    var data = bufferScope.Buffer.ExtractImage();
                    _currentFrame = data;
                }
            }
            finally
            {
                bufferScope.ReleaseNow();
            }
        }

        public byte[]? GetCurrentFrame()
        {
            lock (_frameLock)
            {
                return _currentFrame?.ToArray();
            }
        }
    }
}
