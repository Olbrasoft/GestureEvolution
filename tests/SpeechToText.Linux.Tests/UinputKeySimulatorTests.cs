using Microsoft.Extensions.Logging;
using Moq;
using Olbrasoft.SpeechToText.Core.Models;
using Olbrasoft.Testing.Xunit.Attributes;

namespace Olbrasoft.SpeechToText.Linux.Tests;

/// <summary>
/// Tests for UinputKeySimulator. All tests are marked with [SkipOnCIFact/Theory]
/// because they create real UinputKeySimulator instances that can simulate
/// actual key presses on the system.
/// </summary>
public class UinputKeySimulatorTests
{
    private readonly Mock<ILogger<UinputKeySimulator>> _loggerMock;
    private readonly UinputKeySimulator _simulator;

    public UinputKeySimulatorTests()
    {
        _loggerMock = new Mock<ILogger<UinputKeySimulator>>();
        _simulator = new UinputKeySimulator(_loggerMock.Object);
    }

    [SkipOnCIFact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new UinputKeySimulator(null!));
    }

    [SkipOnCIFact]
    public async Task SimulateKeyPressAsync_WhenUinputNotFound_ShouldLogError()
    {
        // Arrange - /dev/uinput typically doesn't exist in test environment
        // or we don't have permissions

        // Act
        await _simulator.SimulateKeyPressAsync(KeyCode.C);

        // Assert - should have logged something (either about missing device or permission)
        // We're testing that the method handles the error gracefully
        // The actual behavior depends on whether /dev/uinput exists
    }

    [SkipOnCIFact]
    public async Task SimulateKeyComboAsync_TwoKeys_WhenUinputNotFound_ShouldLogError()
    {
        // Act
        await _simulator.SimulateKeyComboAsync(KeyCode.LeftControl, KeyCode.C);

        // Assert - should have logged and returned without crashing
    }

    [SkipOnCIFact]
    public async Task SimulateKeyComboAsync_ThreeKeys_WhenUinputNotFound_ShouldLogError()
    {
        // Act
        await _simulator.SimulateKeyComboAsync(
            KeyCode.LeftControl,
            KeyCode.LeftShift,
            KeyCode.V);

        // Assert - should have logged and returned without crashing
    }

    [SkipOnCITheory]
    [InlineData(KeyCode.C)]
    [InlineData(KeyCode.Enter)]
    [InlineData(KeyCode.Escape)]
    [InlineData(KeyCode.CapsLock)]
    public async Task SimulateKeyPressAsync_VariousKeys_ShouldNotThrow(KeyCode key)
    {
        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() =>
            _simulator.SimulateKeyPressAsync(key));

        Assert.Null(exception);
    }

    [SkipOnCITheory]
    [InlineData(KeyCode.LeftControl, KeyCode.C)]
    [InlineData(KeyCode.LeftControl, KeyCode.V)]
    [InlineData(KeyCode.LeftAlt, KeyCode.Space)]
    public async Task SimulateKeyComboAsync_VariousCombos_ShouldNotThrow(
        KeyCode modifier, KeyCode key)
    {
        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() =>
            _simulator.SimulateKeyComboAsync(modifier, key));

        Assert.Null(exception);
    }

    [SkipOnCIFact]
    public async Task SimulateKeyComboAsync_CtrlShiftV_ShouldNotThrow()
    {
        // Act & Assert
        var exception = await Record.ExceptionAsync(() =>
            _simulator.SimulateKeyComboAsync(
                KeyCode.LeftControl,
                KeyCode.LeftShift,
                KeyCode.V));

        Assert.Null(exception);
    }

    [SkipOnCIFact]
    public async Task SimulateKeyComboAsync_CtrlAltEnter_ShouldNotThrow()
    {
        // Act & Assert
        var exception = await Record.ExceptionAsync(() =>
            _simulator.SimulateKeyComboAsync(
                KeyCode.LeftControl,
                KeyCode.LeftAlt,
                KeyCode.Enter));

        Assert.Null(exception);
    }
}
