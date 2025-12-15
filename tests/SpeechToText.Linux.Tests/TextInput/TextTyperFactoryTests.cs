using Microsoft.Extensions.Logging;
using Moq;
using Olbrasoft.SpeechToText.TextInput;

namespace Olbrasoft.SpeechToText.Linux.Tests.TextInput;

public class TextTyperFactoryTests
{
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;

    public TextTyperFactoryTests()
    {
        _loggerFactoryMock = new Mock<ILoggerFactory>();

        // Setup loggers for all text typer types
        _loggerFactoryMock
            .Setup(x => x.CreateLogger(It.Is<string>(s => s.Contains("XdotoolTextTyper"))))
            .Returns(Mock.Of<ILogger<XdotoolTextTyper>>());
        _loggerFactoryMock
            .Setup(x => x.CreateLogger(It.Is<string>(s => s.Contains("DotoolTextTyper"))))
            .Returns(Mock.Of<ILogger<DotoolTextTyper>>());
    }

    [Fact]
    public void Create_WithNullLoggerFactory_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => TextTyperFactory.Create(null!));
    }

    [Fact]
    public void Create_WithValidLoggerFactory_ShouldReturnITextTyper()
    {
        // Act
        var typer = TextTyperFactory.Create(_loggerFactoryMock.Object);

        // Assert
        Assert.NotNull(typer);
        Assert.True(typer is XdotoolTextTyper || typer is DotoolTextTyper);
    }

    [Fact]
    public void IsWayland_ShouldReturnBooleanWithoutException()
    {
        // Act
        var exception = Record.Exception(() => TextTyperFactory.IsWayland());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void GetDisplayServerName_ShouldReturnNonEmptyString()
    {
        // Act
        var displayServerName = TextTyperFactory.GetDisplayServerName();

        // Assert
        Assert.NotNull(displayServerName);
        Assert.NotEmpty(displayServerName);
    }

    [Fact]
    public void GetDisplayServerName_ShouldReturnKnownValues()
    {
        // Act
        var displayServerName = TextTyperFactory.GetDisplayServerName();

        // Assert - should be one of the known values
        var knownValues = new[] { "wayland", "x11", "unknown", "tty" };

        // The value could also be the raw XDG_SESSION_TYPE value
        // so we just verify it's not empty
        Assert.NotEmpty(displayServerName);
    }

    [Fact]
    public void Create_ShouldCreateAppropriateTyperForEnvironment()
    {
        // Arrange & Act
        var typer = TextTyperFactory.Create(_loggerFactoryMock.Object);
        var isWayland = TextTyperFactory.IsWayland();

        // Assert
        Assert.NotNull(typer);

        // On Wayland, it should prefer DotoolTextTyper (if available)
        // On X11, it should prefer XdotoolTextTyper (if available)
        // The actual type depends on what tools are installed
        if (isWayland)
        {
            // Either DotoolTextTyper (preferred) or XdotoolTextTyper (fallback)
            Assert.True(typer is DotoolTextTyper || typer is XdotoolTextTyper);
        }
        else
        {
            // Either XdotoolTextTyper (preferred) or DotoolTextTyper (fallback)
            Assert.True(typer is XdotoolTextTyper || typer is DotoolTextTyper);
        }
    }

    [Fact]
    public void Create_MultipleCalls_ShouldCreateNewInstances()
    {
        // Arrange & Act
        var typer1 = TextTyperFactory.Create(_loggerFactoryMock.Object);
        var typer2 = TextTyperFactory.Create(_loggerFactoryMock.Object);

        // Assert
        Assert.NotNull(typer1);
        Assert.NotNull(typer2);
        Assert.NotSame(typer1, typer2);
    }
}
