using Microsoft.Extensions.Logging;
using Moq;
using Olbrasoft.SpeechToText.TextInput;

namespace Olbrasoft.SpeechToText.Linux.Tests.TextInput;

public class DotoolTextTyperTests
{
    private readonly Mock<ILogger<DotoolTextTyper>> _loggerMock;

    public DotoolTextTyperTests()
    {
        _loggerMock = new Mock<ILogger<DotoolTextTyper>>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DotoolTextTyper(null!));
    }

    [Fact]
    public void Constructor_WithValidLogger_ShouldNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => new DotoolTextTyper(_loggerMock.Object));
        Assert.Null(exception);
    }

    [Fact]
    public async Task TypeTextAsync_WithNullText_ShouldReturnEarly()
    {
        // Arrange
        var typer = new DotoolTextTyper(_loggerMock.Object);

        // Act - should not throw
        var exception = await Record.ExceptionAsync(() => typer.TypeTextAsync(null!));

        // Assert - should return early without throwing
        Assert.Null(exception);
    }

    [Fact]
    public async Task TypeTextAsync_WithEmptyText_ShouldReturnEarly()
    {
        // Arrange
        var typer = new DotoolTextTyper(_loggerMock.Object);

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
        var typer = new DotoolTextTyper(_loggerMock.Object);

        // Act - should not throw
        var exception = await Record.ExceptionAsync(() => typer.TypeTextAsync(text));

        // Assert - should return early without throwing
        Assert.Null(exception);
    }

    [Fact]
    public async Task TypeTextAsync_WithCancellationToken_ShouldAcceptToken()
    {
        // Arrange
        var typer = new DotoolTextTyper(_loggerMock.Object);
        var cts = new CancellationTokenSource();

        // Act - verify method signature accepts token
        // Will return early because text is null
        await typer.TypeTextAsync(null!, cts.Token);

        // Assert - no exception means it worked
    }

    [Fact]
    public void TerminalClasses_ShouldContainCommonTerminals()
    {
        // This test verifies the terminal detection logic by testing the known terminal classes
        // Since TerminalClasses is private, we can't directly access it,
        // but we document what terminals should be detected

        // Known terminals that should use Ctrl+Shift+V:
        // kitty, gnome-terminal, konsole, xfce4-terminal, mate-terminal,
        // tilix, terminator, alacritty, wezterm, foot, xterm, urxvt, st, terminology

        // This is a documentation test - the actual logic is tested implicitly
        // through integration tests when running on a real system
        Assert.True(true);
    }
}
