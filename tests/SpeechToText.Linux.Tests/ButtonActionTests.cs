using Moq;
using Olbrasoft.SpeechToText;
using Olbrasoft.SpeechToText.Actions;
using Olbrasoft.SpeechToText.Core.Interfaces;
using Olbrasoft.SpeechToText.Core.Models;

namespace Olbrasoft.SpeechToText.Linux.Tests;

public class NoActionTests
{
    [Fact]
    public void Instance_ShouldReturnSameInstance()
    {
        // Act
        var instance1 = NoAction.Instance;
        var instance2 = NoAction.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void Name_ShouldReturnNoAction()
    {
        // Assert
        Assert.Equal("NoAction", NoAction.Instance.Name);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCompleteImmediately()
    {
        // Act
        await NoAction.Instance.ExecuteAsync();

        // Assert - no exception means success
    }

    [Fact]
    public void ImplementsIButtonAction()
    {
        // Assert
        Assert.IsAssignableFrom<IButtonAction>(NoAction.Instance);
    }
}

public class KeyPressActionTests
{
    private readonly Mock<IKeySimulator> _keySimulatorMock;
    private readonly Mock<IKeyboardMonitor> _keyboardMonitorMock;

    public KeyPressActionTests()
    {
        _keySimulatorMock = new Mock<IKeySimulator>();
        _keyboardMonitorMock = new Mock<IKeyboardMonitor>();
    }

    [Fact]
    public void Constructor_WithNullKeySimulator_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new KeyPressAction(null!, _keyboardMonitorMock.Object, KeyCode.CapsLock, "Test"));
    }

    [Fact]
    public void Constructor_WithNullKeyboardMonitor_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new KeyPressAction(_keySimulatorMock.Object, null!, KeyCode.CapsLock, "Test"));
    }

    [Fact]
    public void Name_ShouldReturnProvidedName()
    {
        // Arrange
        var action = new KeyPressAction(
            _keySimulatorMock.Object,
            _keyboardMonitorMock.Object,
            KeyCode.CapsLock,
            "TestAction");

        // Assert
        Assert.Equal("TestAction", action.Name);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCallSimulateKeyPressAsync()
    {
        // Arrange
        var action = new KeyPressAction(
            _keySimulatorMock.Object,
            _keyboardMonitorMock.Object,
            KeyCode.CapsLock,
            "CapsLockPress");

        _keySimulatorMock.Setup(k => k.SimulateKeyPressAsync(KeyCode.CapsLock))
            .Returns(Task.CompletedTask);

        // Act
        await action.ExecuteAsync();

        // Assert
        _keySimulatorMock.Verify(k => k.SimulateKeyPressAsync(KeyCode.CapsLock), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithRaiseReleaseEvent_ShouldRaiseKeyReleasedEvent()
    {
        // Arrange
        var action = new KeyPressAction(
            _keySimulatorMock.Object,
            _keyboardMonitorMock.Object,
            KeyCode.CapsLock,
            "CapsLockPress",
            raiseReleaseEvent: true);

        _keySimulatorMock.Setup(k => k.SimulateKeyPressAsync(KeyCode.CapsLock))
            .Returns(Task.CompletedTask);

        // Act
        await action.ExecuteAsync();

        // Assert
        _keyboardMonitorMock.Verify(k => k.RaiseKeyReleasedEvent(KeyCode.CapsLock), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutRaiseReleaseEvent_ShouldNotRaiseKeyReleasedEvent()
    {
        // Arrange
        var action = new KeyPressAction(
            _keySimulatorMock.Object,
            _keyboardMonitorMock.Object,
            KeyCode.CapsLock,
            "CapsLockPress",
            raiseReleaseEvent: false);

        _keySimulatorMock.Setup(k => k.SimulateKeyPressAsync(KeyCode.CapsLock))
            .Returns(Task.CompletedTask);

        // Act
        await action.ExecuteAsync();

        // Assert
        _keyboardMonitorMock.Verify(k => k.RaiseKeyReleasedEvent(It.IsAny<KeyCode>()), Times.Never);
    }

    [Fact]
    public void ImplementsIButtonAction()
    {
        // Arrange
        var action = new KeyPressAction(
            _keySimulatorMock.Object,
            _keyboardMonitorMock.Object,
            KeyCode.CapsLock,
            "Test");

        // Assert
        Assert.IsAssignableFrom<IButtonAction>(action);
    }
}

public class KeyComboActionTests
{
    private readonly Mock<IKeySimulator> _keySimulatorMock;

    public KeyComboActionTests()
    {
        _keySimulatorMock = new Mock<IKeySimulator>();
    }

    [Fact]
    public void Constructor_WithNullKeySimulator_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new KeyComboAction(null!, KeyCode.LeftControl, KeyCode.C, "Copy"));
    }

    [Fact]
    public void Name_ShouldReturnProvidedName()
    {
        // Arrange
        var action = new KeyComboAction(
            _keySimulatorMock.Object,
            KeyCode.LeftControl,
            KeyCode.V,
            "Paste");

        // Assert
        Assert.Equal("Paste", action.Name);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCallSimulateKeyComboAsync()
    {
        // Arrange
        var action = new KeyComboAction(
            _keySimulatorMock.Object,
            KeyCode.LeftControl,
            KeyCode.C,
            "Copy");

        _keySimulatorMock.Setup(k => k.SimulateKeyComboAsync(KeyCode.LeftControl, KeyCode.C))
            .Returns(Task.CompletedTask);

        // Act
        await action.ExecuteAsync();

        // Assert
        _keySimulatorMock.Verify(k => k.SimulateKeyComboAsync(KeyCode.LeftControl, KeyCode.C), Times.Once);
    }

    [Fact]
    public void ImplementsIButtonAction()
    {
        // Arrange
        var action = new KeyComboAction(
            _keySimulatorMock.Object,
            KeyCode.LeftControl,
            KeyCode.C,
            "Test");

        // Assert
        Assert.IsAssignableFrom<IButtonAction>(action);
    }
}

public class KeyComboWithTwoModifiersActionTests
{
    private readonly Mock<IKeySimulator> _keySimulatorMock;

    public KeyComboWithTwoModifiersActionTests()
    {
        _keySimulatorMock = new Mock<IKeySimulator>();
    }

    [Fact]
    public void Constructor_WithNullKeySimulator_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new KeyComboWithTwoModifiersAction(
                null!,
                KeyCode.LeftControl,
                KeyCode.LeftShift,
                KeyCode.Escape,
                "Test"));
    }

    [Fact]
    public void Name_ShouldReturnProvidedName()
    {
        // Arrange
        var action = new KeyComboWithTwoModifiersAction(
            _keySimulatorMock.Object,
            KeyCode.LeftControl,
            KeyCode.LeftShift,
            KeyCode.Escape,
            "Ctrl+Shift+Esc");

        // Assert
        Assert.Equal("Ctrl+Shift+Esc", action.Name);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCallSimulateKeyComboAsyncWithThreeKeys()
    {
        // Arrange
        var action = new KeyComboWithTwoModifiersAction(
            _keySimulatorMock.Object,
            KeyCode.LeftControl,
            KeyCode.LeftShift,
            KeyCode.Escape,
            "Ctrl+Shift+Esc");

        _keySimulatorMock.Setup(k => k.SimulateKeyComboAsync(
            KeyCode.LeftControl, KeyCode.LeftShift, KeyCode.Escape))
            .Returns(Task.CompletedTask);

        // Act
        await action.ExecuteAsync();

        // Assert
        _keySimulatorMock.Verify(k => k.SimulateKeyComboAsync(
            KeyCode.LeftControl, KeyCode.LeftShift, KeyCode.Escape), Times.Once);
    }

    [Fact]
    public void ImplementsIButtonAction()
    {
        // Arrange
        var action = new KeyComboWithTwoModifiersAction(
            _keySimulatorMock.Object,
            KeyCode.LeftControl,
            KeyCode.LeftAlt,
            KeyCode.F8,
            "Test");

        // Assert
        Assert.IsAssignableFrom<IButtonAction>(action);
    }
}

public class ShellCommandActionTests
{
    [Fact]
    public void Constructor_WithNullCommand_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ShellCommandAction(null!, "Test"));
    }

    [Fact]
    public void Name_ShouldReturnProvidedName()
    {
        // Arrange
        var action = new ShellCommandAction("echo test", "EchoTest");

        // Assert
        Assert.Equal("EchoTest", action.Name);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCompleteWithoutException()
    {
        // Arrange
        var action = new ShellCommandAction("echo 'test'", "Echo");

        // Act & Assert - should not throw
        await action.ExecuteAsync();
    }

    [Fact]
    public void ImplementsIButtonAction()
    {
        // Arrange
        var action = new ShellCommandAction("ls", "List");

        // Assert
        Assert.IsAssignableFrom<IButtonAction>(action);
    }
}
