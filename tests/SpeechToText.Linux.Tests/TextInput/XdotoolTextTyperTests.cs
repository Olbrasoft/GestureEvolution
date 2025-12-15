using Microsoft.Extensions.Logging;
using Moq;
using Olbrasoft.SpeechToText.TextInput;
using Olbrasoft.Testing.Xunit.Attributes;

namespace Olbrasoft.SpeechToText.Linux.Tests.TextInput;

/// <summary>
/// Tests for XdotoolTextTyper. Tests that create real instances are marked
/// with [SkipOnCIFact/Theory] to prevent GUI process spawning on CI.
/// </summary>
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

    [SkipOnCIFact]
    public void Constructor_WithValidLogger_ShouldNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => new XdotoolTextTyper(_loggerMock.Object));
        Assert.Null(exception);
    }

    [SkipOnCITheory]
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

    [SkipOnCIFact]
    public async Task TypeTextAsync_WithNullText_ShouldReturnEarly()
    {
        // Arrange
        var typer = new XdotoolTextTyper(_loggerMock.Object);

        // Act - should not throw
        var exception = await Record.ExceptionAsync(() => typer.TypeTextAsync(null!));

        // Assert - should return early without throwing
        Assert.Null(exception);
    }

    [SkipOnCIFact]
    public async Task TypeTextAsync_WithEmptyText_ShouldReturnEarly()
    {
        // Arrange
        var typer = new XdotoolTextTyper(_loggerMock.Object);

        // Act - should not throw
        var exception = await Record.ExceptionAsync(() => typer.TypeTextAsync(string.Empty));

        // Assert - should return early without throwing
        Assert.Null(exception);
    }

    [SkipOnCITheory]
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

    [SkipOnCIFact]
    public void IsAvailable_ShouldReturnBooleanWithoutException()
    {
        // Arrange
        var typer = new XdotoolTextTyper(_loggerMock.Object);

        // Act
        var exception = Record.Exception(() => _ = typer.IsAvailable);

        // Assert - should not throw
        Assert.Null(exception);
    }

    [SkipOnCIFact]
    public async Task TypeTextAsync_WhenNotAvailable_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var typer = new XdotoolTextTyper(_loggerMock.Object);

        // Only test if xdotool is not available
        if (typer.IsAvailable)
        {
            return; // Skip test if xdotool is installed
        }

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            typer.TypeTextAsync("test text"));
    }

    [SkipOnCIFact]
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
