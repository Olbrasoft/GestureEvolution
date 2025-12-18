using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.Native;

namespace Olbrasoft.SpeechToText;

/// <summary>
/// Linux uinput-based key simulator using native C# P/Invoke.
/// Creates a virtual keyboard device to simulate key presses.
/// </summary>
public class UinputKeySimulator : IKeySimulator
{
    private readonly ILogger<UinputKeySimulator> _logger;
    private const string UinputPath = "/dev/uinput";
    private const string DeviceName = "push-to-talk-kbd";

    // Timing delays (in milliseconds)
    private const int DeviceSetupDelayMs = 100;
    private const int KeyPressDelayMs = 50;
    private const int ModifierDelayMs = 20;
    private const int DeviceCleanupDelayMs = 100;

    public UinputKeySimulator(ILogger<UinputKeySimulator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task SimulateKeyPressAsync(KeyCode key)
    {
        try
        {
            _logger.LogInformation("Simulating key press: {Key}", key);

            if (!File.Exists(UinputPath))
            {
                _logger.LogError("uinput device not found at {Path}", UinputPath);
                return;
            }

            var keyCode = (ushort)key;
            await SimulateKeysAsync([keyCode], [keyCode]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating key press: {Key}", key);
        }
    }

    /// <inheritdoc/>
    public async Task SimulateKeyComboAsync(KeyCode modifier, KeyCode key)
    {
        try
        {
            _logger.LogInformation("Simulating key combo: {Modifier}+{Key}", modifier, key);

            if (!File.Exists(UinputPath))
            {
                _logger.LogError("uinput device not found at {Path}", UinputPath);
                return;
            }

            var modCode = (ushort)modifier;
            var keyCode = (ushort)key;

            // Keys to enable (all unique keys needed)
            var keysToEnable = new[] { modCode, keyCode };

            // Press sequence: modifier down, key down, key up, modifier up
            await SimulateKeyComboInternalAsync(keysToEnable, [modCode], keyCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating key combo: {Modifier}+{Key}", modifier, key);
        }
    }

    /// <inheritdoc/>
    public async Task SimulateKeyComboAsync(KeyCode modifier1, KeyCode modifier2, KeyCode key)
    {
        try
        {
            _logger.LogInformation("Simulating key combo: {Modifier1}+{Modifier2}+{Key}", modifier1, modifier2, key);

            if (!File.Exists(UinputPath))
            {
                _logger.LogError("uinput device not found at {Path}", UinputPath);
                return;
            }

            var mod1Code = (ushort)modifier1;
            var mod2Code = (ushort)modifier2;
            var keyCode = (ushort)key;

            // Keys to enable (all unique keys needed)
            var keysToEnable = new[] { mod1Code, mod2Code, keyCode };

            // Press sequence: modifier1 down, modifier2 down, key down, key up, modifier2 up, modifier1 up
            await SimulateKeyComboInternalAsync(keysToEnable, [mod1Code, mod2Code], keyCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating key combo: {Modifier1}+{Modifier2}+{Key}", modifier1, modifier2, key);
        }
    }

    /// <summary>
    /// Simulates a single key press/release sequence.
    /// </summary>
    private async Task SimulateKeysAsync(ushort[] keysToEnable, ushort[] keysToPress)
    {
        var fd = -1;
        try
        {
            fd = OpenUinputDevice();
            if (fd < 0)
            {
                _logger.LogError("Failed to open uinput device");
                return;
            }

            // Setup device
            SetupKeyboardDevice(fd, keysToEnable);

            // Wait for device to be ready
            await Task.Delay(DeviceSetupDelayMs);

            // Press all keys
            foreach (var key in keysToPress)
            {
                SendKeyEvent(fd, key, 1); // Press
                SendSyncEvent(fd);
            }

            await Task.Delay(KeyPressDelayMs);

            // Release all keys in reverse order
            for (int i = keysToPress.Length - 1; i >= 0; i--)
            {
                SendKeyEvent(fd, keysToPress[i], 0); // Release
                SendSyncEvent(fd);
            }

            await Task.Delay(DeviceCleanupDelayMs);

            _logger.LogInformation("Successfully simulated key press");
        }
        finally
        {
            if (fd >= 0)
            {
                DestroyAndCloseDevice(fd);
            }
        }
    }

    /// <summary>
    /// Simulates a key combination with modifiers.
    /// </summary>
    private async Task SimulateKeyComboInternalAsync(ushort[] keysToEnable, ushort[] modifiers, ushort key)
    {
        var fd = -1;
        try
        {
            fd = OpenUinputDevice();
            if (fd < 0)
            {
                _logger.LogError("Failed to open uinput device");
                return;
            }

            // Setup device
            SetupKeyboardDevice(fd, keysToEnable);

            // Wait for device to be ready
            await Task.Delay(DeviceSetupDelayMs);

            // Press modifiers in order
            foreach (var mod in modifiers)
            {
                SendKeyEvent(fd, mod, 1); // Press modifier
                SendSyncEvent(fd);
                await Task.Delay(ModifierDelayMs);
            }

            // Press main key
            SendKeyEvent(fd, key, 1); // Press
            SendSyncEvent(fd);
            await Task.Delay(KeyPressDelayMs);

            // Release main key
            SendKeyEvent(fd, key, 0); // Release
            SendSyncEvent(fd);
            await Task.Delay(ModifierDelayMs);

            // Release modifiers in reverse order
            for (int i = modifiers.Length - 1; i >= 0; i--)
            {
                SendKeyEvent(fd, modifiers[i], 0); // Release modifier
                SendSyncEvent(fd);
                await Task.Delay(ModifierDelayMs);
            }

            await Task.Delay(DeviceCleanupDelayMs);

            _logger.LogInformation("Successfully simulated key combo");
        }
        finally
        {
            if (fd >= 0)
            {
                DestroyAndCloseDevice(fd);
            }
        }
    }

    /// <summary>
    /// Opens the uinput device for writing.
    /// </summary>
    private int OpenUinputDevice()
    {
        var fd = LinuxInterop.Open(UinputPath, LinuxInterop.O_WRONLY | LinuxInterop.O_NONBLOCK);
        if (fd < 0)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("Failed to open {Path}: errno={Error}", UinputPath, error);
        }
        return fd;
    }

    /// <summary>
    /// Sets up the virtual keyboard device with the specified keys enabled.
    /// </summary>
    private void SetupKeyboardDevice(int fd, ushort[] keyCodes)
    {
        // Enable EV_KEY event type
        var result = LinuxInterop.Ioctl(fd, LinuxInterop.UI_SET_EVBIT, LinuxInterop.EV_KEY);
        if (result < 0)
        {
            _logger.LogWarning("Failed to set EV_KEY bit: errno={Error}", Marshal.GetLastWin32Error());
        }

        // Enable each key code
        foreach (var keyCode in keyCodes)
        {
            result = LinuxInterop.Ioctl(fd, LinuxInterop.UI_SET_KEYBIT, keyCode);
            if (result < 0)
            {
                _logger.LogWarning("Failed to set key bit {KeyCode}: errno={Error}", keyCode, Marshal.GetLastWin32Error());
            }
        }

        // Write uinput_user_dev structure
        var userDev = UinputUserDev.Create(DeviceName);
        WriteStruct(fd, ref userDev);

        // Create the device
        result = LinuxInterop.Ioctl(fd, LinuxInterop.UI_DEV_CREATE, 0);
        if (result < 0)
        {
            _logger.LogWarning("Failed to create device: errno={Error}", Marshal.GetLastWin32Error());
        }
    }

    /// <summary>
    /// Sends a key event to the uinput device.
    /// </summary>
    private void SendKeyEvent(int fd, ushort keyCode, int value)
    {
        var ev = InputEvent.Create(LinuxInterop.EV_KEY, keyCode, value);
        WriteStruct(fd, ref ev);
    }

    /// <summary>
    /// Sends a sync event to flush pending events.
    /// </summary>
    private void SendSyncEvent(int fd)
    {
        var ev = InputEvent.Create(LinuxInterop.EV_SYN, LinuxInterop.SYN_REPORT, 0);
        WriteStruct(fd, ref ev);
    }

    /// <summary>
    /// Destroys the virtual device and closes the file descriptor.
    /// </summary>
    private void DestroyAndCloseDevice(int fd)
    {
        LinuxInterop.Ioctl(fd, LinuxInterop.UI_DEV_DESTROY, 0);
        LinuxInterop.Close(fd);
    }

    /// <summary>
    /// Writes a struct to the file descriptor.
    /// </summary>
    private static unsafe void WriteStruct<T>(int fd, ref T data) where T : unmanaged
    {
        fixed (T* ptr = &data)
        {
            var size = (nuint)sizeof(T);
            LinuxInterop.Write(fd, (nint)ptr, size);
        }
    }
}
