using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText;

/// <summary>
/// Linux uinput-based key simulator using Python for low-level device control.
/// </summary>
public class UinputKeySimulator : IKeySimulator
{
    private readonly ILogger<UinputKeySimulator> _logger;
    private const string UinputPath = "/dev/uinput";

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

            var keyCode = (int)key;
            var script = GenerateSingleKeyScript(keyCode);

            await ExecutePythonScriptAsync(script, $"key press: {key}");
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

            var modifierCode = (int)modifier;
            var keyCode = (int)key;
            var script = GenerateTwoKeyComboScript(modifierCode, keyCode);

            await ExecutePythonScriptAsync(script, $"key combo: {modifier}+{key}");
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

            var mod1Code = (int)modifier1;
            var mod2Code = (int)modifier2;
            var keyCode = (int)key;
            var script = GenerateThreeKeyComboScript(mod1Code, mod2Code, keyCode);

            await ExecutePythonScriptAsync(script, $"key combo: {modifier1}+{modifier2}+{key}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating key combo: {Modifier1}+{Modifier2}+{Key}", modifier1, modifier2, key);
        }
    }

    private async Task ExecutePythonScriptAsync(string script, string operationDescription)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = $"-c \"{script.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
            var stderr = await process.StandardError.ReadToEndAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError("Failed to simulate {Operation}. Exit code: {ExitCode}, Error: {Error}",
                    operationDescription, process.ExitCode, stderr);
            }
            else
            {
                _logger.LogInformation("Successfully simulated {Operation}", operationDescription);
            }
        }
    }

    private static string GenerateSingleKeyScript(int keyCode) => $@"
import os
import time
import struct
import fcntl

# uinput constants
UI_SET_EVBIT = 0x40045564
UI_SET_KEYBIT = 0x40045565
UI_DEV_CREATE = 0x5501
UI_DEV_DESTROY = 0x5502
EV_KEY = 0x01
EV_SYN = 0x00
SYN_REPORT = 0x00

# Open uinput
fd = os.open('/dev/uinput', os.O_WRONLY | os.O_NONBLOCK)

# Enable EV_KEY
fcntl.ioctl(fd, UI_SET_EVBIT, EV_KEY)

# Enable the specific key
fcntl.ioctl(fd, UI_SET_KEYBIT, {keyCode})

# uinput_user_dev structure (legacy)
name = b'speech-to-text-kbd'
name = name + b'\x00' * (80 - len(name))
user_dev = name + struct.pack('<HHHHI', 0x03, 0x1234, 0x5678, 0x0001, 0)
user_dev = user_dev + b'\x00' * (4 * 64 * 4)

os.write(fd, user_dev)
fcntl.ioctl(fd, UI_DEV_CREATE)

time.sleep(0.1)

def send_event(fd, ev_type, code, value):
    event = struct.pack('<QQHHi', 0, 0, ev_type, code, value)
    os.write(fd, event)

send_event(fd, EV_KEY, {keyCode}, 1)  # Press
send_event(fd, EV_SYN, SYN_REPORT, 0)

time.sleep(0.05)

send_event(fd, EV_KEY, {keyCode}, 0)  # Release
send_event(fd, EV_SYN, SYN_REPORT, 0)

time.sleep(0.1)

fcntl.ioctl(fd, UI_DEV_DESTROY)
os.close(fd)
";

    private static string GenerateTwoKeyComboScript(int modifierCode, int keyCode) => $@"
import os
import time
import struct
import fcntl

# uinput constants
UI_SET_EVBIT = 0x40045564
UI_SET_KEYBIT = 0x40045565
UI_DEV_CREATE = 0x5501
UI_DEV_DESTROY = 0x5502
EV_KEY = 0x01
EV_SYN = 0x00
SYN_REPORT = 0x00

# Open uinput
fd = os.open('/dev/uinput', os.O_WRONLY | os.O_NONBLOCK)

# Enable EV_KEY
fcntl.ioctl(fd, UI_SET_EVBIT, EV_KEY)

# Enable both keys
fcntl.ioctl(fd, UI_SET_KEYBIT, {modifierCode})
fcntl.ioctl(fd, UI_SET_KEYBIT, {keyCode})

# Create uinput device
name = b'speech-to-text-kbd'
name = name + b'\x00' * (80 - len(name))
user_dev = name + struct.pack('<HHHHI', 0x03, 0x1234, 0x5678, 0x0001, 0)
user_dev = user_dev + b'\x00' * (4 * 64 * 4)

os.write(fd, user_dev)
fcntl.ioctl(fd, UI_DEV_CREATE)

time.sleep(0.1)

def send_event(fd, ev_type, code, value):
    event = struct.pack('<QQHHi', 0, 0, ev_type, code, value)
    os.write(fd, event)

# Press modifier
send_event(fd, EV_KEY, {modifierCode}, 1)
send_event(fd, EV_SYN, SYN_REPORT, 0)
time.sleep(0.02)

# Press key
send_event(fd, EV_KEY, {keyCode}, 1)
send_event(fd, EV_SYN, SYN_REPORT, 0)
time.sleep(0.05)

# Release key
send_event(fd, EV_KEY, {keyCode}, 0)
send_event(fd, EV_SYN, SYN_REPORT, 0)
time.sleep(0.02)

# Release modifier
send_event(fd, EV_KEY, {modifierCode}, 0)
send_event(fd, EV_SYN, SYN_REPORT, 0)

time.sleep(0.1)

fcntl.ioctl(fd, UI_DEV_DESTROY)
os.close(fd)
";

    private static string GenerateThreeKeyComboScript(int mod1Code, int mod2Code, int keyCode) => $@"
import os
import time
import struct
import fcntl

# uinput constants
UI_SET_EVBIT = 0x40045564
UI_SET_KEYBIT = 0x40045565
UI_DEV_CREATE = 0x5501
UI_DEV_DESTROY = 0x5502
EV_KEY = 0x01
EV_SYN = 0x00
SYN_REPORT = 0x00

# Open uinput
fd = os.open('/dev/uinput', os.O_WRONLY | os.O_NONBLOCK)

# Enable EV_KEY
fcntl.ioctl(fd, UI_SET_EVBIT, EV_KEY)

# Enable all keys
fcntl.ioctl(fd, UI_SET_KEYBIT, {mod1Code})
fcntl.ioctl(fd, UI_SET_KEYBIT, {mod2Code})
fcntl.ioctl(fd, UI_SET_KEYBIT, {keyCode})

# Create uinput device
name = b'speech-to-text-kbd'
name = name + b'\x00' * (80 - len(name))
user_dev = name + struct.pack('<HHHHI', 0x03, 0x1234, 0x5678, 0x0001, 0)
user_dev = user_dev + b'\x00' * (4 * 64 * 4)

os.write(fd, user_dev)
fcntl.ioctl(fd, UI_DEV_CREATE)

time.sleep(0.1)

def send_event(fd, ev_type, code, value):
    event = struct.pack('<QQHHi', 0, 0, ev_type, code, value)
    os.write(fd, event)

# Press modifier1
send_event(fd, EV_KEY, {mod1Code}, 1)
send_event(fd, EV_SYN, SYN_REPORT, 0)
time.sleep(0.02)

# Press modifier2
send_event(fd, EV_KEY, {mod2Code}, 1)
send_event(fd, EV_SYN, SYN_REPORT, 0)
time.sleep(0.02)

# Press key
send_event(fd, EV_KEY, {keyCode}, 1)
send_event(fd, EV_SYN, SYN_REPORT, 0)
time.sleep(0.05)

# Release key
send_event(fd, EV_KEY, {keyCode}, 0)
send_event(fd, EV_SYN, SYN_REPORT, 0)
time.sleep(0.02)

# Release modifier2
send_event(fd, EV_KEY, {mod2Code}, 0)
send_event(fd, EV_SYN, SYN_REPORT, 0)
time.sleep(0.02)

# Release modifier1
send_event(fd, EV_KEY, {mod1Code}, 0)
send_event(fd, EV_SYN, SYN_REPORT, 0)

time.sleep(0.1)

fcntl.ioctl(fd, UI_DEV_DESTROY)
os.close(fd)
";
}
