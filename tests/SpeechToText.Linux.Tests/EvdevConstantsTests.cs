using Olbrasoft.SpeechToText;

namespace Olbrasoft.SpeechToText.Linux.Tests;

public class EvdevConstantsTests
{
    [Fact]
    public void EVIOCGRAB_ShouldHaveCorrectValue()
    {
        // Assert - _IOW('E', 0x90, int) = 0x40044590
        Assert.Equal(0x40044590u, EvdevConstants.EVIOCGRAB);
    }

    [Fact]
    public void InputEventSize_ShouldBe24Bytes()
    {
        // Assert - input_event on 64-bit: timeval (16) + type (2) + code (2) + value (4)
        Assert.Equal(24, EvdevConstants.InputEventSize);
    }

    [Fact]
    public void TimevalOffset_ShouldBe16Bytes()
    {
        // Assert - timeval is 16 bytes on 64-bit systems
        Assert.Equal(16, EvdevConstants.TimevalOffset);
    }

    [Fact]
    public void EV_KEY_ShouldBe1()
    {
        // Assert
        Assert.Equal(0x01, EvdevConstants.EV_KEY);
    }

    [Fact]
    public void KEY_PRESS_ShouldBe1()
    {
        // Assert
        Assert.Equal(1, EvdevConstants.KEY_PRESS);
    }

    [Fact]
    public void KEY_RELEASE_ShouldBe0()
    {
        // Assert
        Assert.Equal(0, EvdevConstants.KEY_RELEASE);
    }

    [Fact]
    public void DevicesPath_ShouldBeCorrect()
    {
        // Assert
        Assert.Equal("/proc/bus/input/devices", EvdevConstants.DevicesPath);
    }

    [Fact]
    public void DefaultReconnectIntervalMs_ShouldBe2000()
    {
        // Assert
        Assert.Equal(2000, EvdevConstants.DefaultReconnectIntervalMs);
    }

    [Fact]
    public void LogIntervalAttempts_ShouldBe30()
    {
        // Assert
        Assert.Equal(30, EvdevConstants.LogIntervalAttempts);
    }
}
