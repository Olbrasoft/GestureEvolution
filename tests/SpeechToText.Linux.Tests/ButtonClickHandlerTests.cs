using Moq;
using Olbrasoft.SpeechToText;
using Olbrasoft.SpeechToText.Actions;

namespace Olbrasoft.SpeechToText.Linux.Tests;

public class ButtonClickHandlerTests : IDisposable
{
    private readonly Mock<IButtonAction> _singleClickActionMock;
    private readonly Mock<IButtonAction> _doubleClickActionMock;
    private readonly Mock<IButtonAction> _tripleClickActionMock;
    private readonly ButtonClickHandler _handler;

    public ButtonClickHandlerTests()
    {
        _singleClickActionMock = new Mock<IButtonAction>();
        _doubleClickActionMock = new Mock<IButtonAction>();
        _tripleClickActionMock = new Mock<IButtonAction>();

        _singleClickActionMock.Setup(a => a.Name).Returns("SingleClick");
        _doubleClickActionMock.Setup(a => a.Name).Returns("DoubleClick");
        _tripleClickActionMock.Setup(a => a.Name).Returns("TripleClick");

        _singleClickActionMock.Setup(a => a.ExecuteAsync()).Returns(Task.CompletedTask);
        _doubleClickActionMock.Setup(a => a.ExecuteAsync()).Returns(Task.CompletedTask);
        _tripleClickActionMock.Setup(a => a.ExecuteAsync()).Returns(Task.CompletedTask);

        _handler = new ButtonClickHandler(
            "TEST",
            _singleClickActionMock.Object,
            _doubleClickActionMock.Object,
            _tripleClickActionMock.Object);
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    [Fact]
    public void Constructor_WithNullSingleClickAction_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ButtonClickHandler(
                "TEST",
                null!,
                _doubleClickActionMock.Object,
                _tripleClickActionMock.Object));
    }

    [Fact]
    public void Constructor_WithNullDoubleClickAction_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ButtonClickHandler(
                "TEST",
                _singleClickActionMock.Object,
                null!,
                _tripleClickActionMock.Object));
    }

    [Fact]
    public void Constructor_WithNullTripleClickAction_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ButtonClickHandler(
                "TEST",
                _singleClickActionMock.Object,
                _doubleClickActionMock.Object,
                null!));
    }

    [Fact]
    public void RegisterClick_WhenDisposed_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _handler.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => _handler.RegisterClick());
    }

    [Fact]
    public void Reset_ShouldNotThrow()
    {
        // Act & Assert - should not throw
        _handler.Reset();
    }

    [Fact]
    public void Dispose_MultipleCalls_ShouldNotThrow()
    {
        // Act & Assert - should not throw
        _handler.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task TripleClick_ShouldExecuteTripleClickAction()
    {
        // Arrange - use a handler with short threshold
        using var handler = new ButtonClickHandler(
            "TEST",
            _singleClickActionMock.Object,
            _doubleClickActionMock.Object,
            _tripleClickActionMock.Object,
            maxClickCount: 3);

        // Act - three rapid clicks
        handler.RegisterClick();
        await Task.Delay(60);
        handler.RegisterClick();
        await Task.Delay(60);
        handler.RegisterClick();

        // Wait for action execution
        await Task.Delay(200);

        // Assert
        _tripleClickActionMock.Verify(a => a.ExecuteAsync(), Times.Once);
    }

    [Fact]
    public void Constructor_WithCustomMaxClickCount_ShouldUseIt()
    {
        // Act
        using var handler = new ButtonClickHandler(
            "TEST",
            _singleClickActionMock.Object,
            _doubleClickActionMock.Object,
            _tripleClickActionMock.Object,
            maxClickCount: 2);

        // Assert - no exception means success
    }
}
