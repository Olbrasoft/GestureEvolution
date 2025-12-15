using Microsoft.Extensions.Logging;
using Moq;
using Olbrasoft.SpeechToText.TextInput;

namespace Olbrasoft.SpeechToText.Linux.Tests.TextInput;

public class XdotoolTextTyperTests
{
    private readonly Mock<ILogger<XdotoolTextTyper>> _loggerMock;

    public XdotoolTextTyperTests()
    {
        _loggerMock = new Mock<ILogger<XdotoolTextTyper>>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new XdotoolTextTyper(null!));
    }

    [Fact]
    public void Constructor_WithValidLogger_ShouldNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => new XdotoolTextTyper(_loggerMock.Object));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void Constructor_WithDelayParameter_ShouldNotThrow(int delay)
    {
        // Act & Assert
        var exception = Record.Exception(() => new XdotoolTextTyper(_loggerMock.Object, delay));
        Assert.Null(exception);
    }

    [Fact]
    public async Task TypeTextAsync_WithNullText_ShouldReturnEarly()
    {
        // Arrange
        var typer = new XdotoolTextTyper(_loggerMock.Object);

        // Act - should not throw
        var exception = await Record.ExceptionAsync(() => typer.TypeTextAsync(null!));

        // Assert - should return early without throwing
        Assert.Null(exception);
    }

    [Fact]
    public async Task TypeTextAsync_WithEmptyText_ShouldReturnEarly()
    {
        // Arrange
        var typer = new XdotoolTextTyper(_loggerMock.Object);

        // Act - should not throw
        var exception = await Record.ExceptionAsync(() => typer.TypeTextAsync(string.Empty));

        // Assert - should return early without throwing
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("   \t\n   ")]
    public async Task TypeTextAsync_WithWhitespaceText_ShouldReturnEarly(string text)
    {
        // Arrange
        var typer = new XdotoolTextTyper(_loggerMock.Object);

        // Act - should not throw
        var exception = await Record.ExceptionAsync(() => typer.TypeTextAsync(text));

        // Assert - should return early without throwing
        Assert.Null(exception);
    }

    [Fact]
    public async Task TypeTextAsync_WithCancellationToken_ShouldAcceptToken()
    {
        // Arrange
        var typer = new XdotoolTextTyper(_loggerMock.Object);
        var cts = new CancellationTokenSource();

        // Act - verify method signature accepts token
        // Will return early because text is null
        await typer.TypeTextAsync(null!, cts.Token);

        // Assert - no exception means it worked
    }
}
