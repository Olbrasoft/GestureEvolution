using System.Text;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// Ensures only one instance of the application is running.
/// </summary>
public sealed class SingleInstanceLock : IDisposable
{
    private readonly string _lockFilePath;
    private FileStream? _lockFile;
    private bool _disposed;

    /// <summary>
    /// Creates a new single instance lock.
    /// </summary>
    /// <param name="lockFilePath">Path to the lock file.</param>
    private SingleInstanceLock(string lockFilePath)
    {
        _lockFilePath = lockFilePath;
    }

    /// <summary>
    /// Gets a value indicating whether the lock was acquired.
    /// </summary>
    public bool IsAcquired => _lockFile != null;

    /// <summary>
    /// Tries to acquire a single instance lock.
    /// </summary>
    /// <param name="lockFilePath">Path to the lock file.</param>
    /// <returns>A lock instance (check IsAcquired to see if lock was obtained).</returns>
    public static SingleInstanceLock TryAcquire(string lockFilePath = "/tmp/speech-to-text.lock")
    {
        var lockInstance = new SingleInstanceLock(lockFilePath);

        try
        {
            lockInstance._lockFile = new FileStream(
                lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            // Write PID to lock file for debugging
            var pidBytes = Encoding.UTF8.GetBytes(Environment.ProcessId.ToString());
            lockInstance._lockFile.Write(pidBytes, 0, pidBytes.Length);
            lockInstance._lockFile.Flush();
        }
        catch (IOException)
        {
            // Lock file is held by another process
            lockInstance._lockFile = null;
        }

        return lockInstance;
    }

    /// <summary>
    /// Releases the lock and deletes the lock file.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _lockFile?.Dispose();
            _lockFile = null;

            if (File.Exists(_lockFilePath))
            {
                File.Delete(_lockFilePath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
