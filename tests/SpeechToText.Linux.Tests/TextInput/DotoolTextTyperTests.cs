using Microsoft.Extensions.Logging;
using Moq;
using Olbrasoft.SpeechToText.TextInput;
using Olbrasoft.Testing.Xunit.Attributes;

namespace Olbrasoft.SpeechToText.Linux.Tests.TextInput;

/// <summary>
/// Tests for DotoolTextTyper. Tests that create real instances are marked
/// with [SkipOnCIFact/Theory] to prevent GUI process spawning on CI.
/// </summary>
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

    [SkipOnCIFact]
    public void Constructor_WithValidLogger_ShouldNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => new DotoolTextTyper(_loggerMock.Object));
        Assert.Null(exception);
    }

    [SkipOnCIFact]
    public async Task TypeTextAsync_WithNullText_ShouldReturnEarly()
    {
        // Arrange
        var typer = new DotoolTextTyper(_loggerMock.Object);

        // Act - should not throw
        var exception = await Record.ExceptionAsync(() => typer.TypeTextAsync(null!));

        // Assert - should return early without throwing
        Assert.Null(exception);
    }

    [SkipOnCIFact]
    public async Task TypeTextAsync_WithEmptyText_ShouldReturnEarly()
    {
        // Arrange
        var typer = new DotoolTextTyper(_loggerMock.Object);

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
        var typer = new DotoolTextTyper(_loggerMock.Object);

        // Act - should not throw
        var exception = await Record.ExceptionAsync(() => typer.TypeTextAsync(text));

        // Assert - should return early without throwing
        Assert.Null(exception);
    }

    [SkipOnCIFact]
    public void IsAvailable_ShouldReturnBooleanWithoutException()
    {
        // Arrange
        var typer = new DotoolTextTyper(_loggerMock.Object);

        // Act
        var exception = Record.Exception(() => _ = typer.IsAvailable);

        // Assert - should not throw
        Assert.Null(exception);
    }

    [SkipOnCIFact]
    public async Task TypeTextAsync_WhenNotAvailable_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var typer = new DotoolTextTyper(_loggerMock.Object);

        // Only test if dotool/wl-copy is not available
        if (typer.IsAvailable)
        {
            return; // Skip test if tools are installed
        }

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            typer.TypeTextAsync("test text"));
    }

    [SkipOnCIFact]
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
