using Microsoft.Extensions.Logging;
using Moq;
using Olbrasoft.SpeechToText.Core.Interfaces;
using Olbrasoft.SpeechToText.TextInput;

namespace Olbrasoft.SpeechToText.Linux.Tests.TextInput;

public class TextTyperFactoryTests
{
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<IEnvironmentProvider> _environmentMock;

    public TextTyperFactoryTests()
    {
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _environmentMock = new Mock<IEnvironmentProvider>();

        // Setup logger factory to return mock loggers
        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger>());
    }

    [Fact]
    public void Constructor_WithNullLoggerFactory_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TextTyperFactory(null!, _environmentMock.Object));
    }

    [Fact]
    public void Constructor_WithNullEnvironmentProvider_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TextTyperFactory(_loggerFactoryMock.Object, null!));
    }

    [Theory]
    [InlineData("wayland", true)]
    [InlineData("WAYLAND", true)]
    [InlineData("Wayland", true)]
    [InlineData("x11", false)]
    [InlineData("X11", false)]
    [InlineData("tty", false)]
    public void IsWayland_WithXdgSessionType_ShouldReturnExpectedValue(string sessionType, bool expected)
    {
        // Arrange
        _environmentMock.Setup(e => e.GetEnvironmentVariable("XDG_SESSION_TYPE"))
            .Returns(sessionType);

        var factory = new TextTyperFactory(_loggerFactoryMock.Object, _environmentMock.Object);

        // Act
        var result = factory.IsWayland();

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsWayland_WithWaylandDisplay_ShouldReturnTrue()
    {
        // Arrange
        _environmentMock.Setup(e => e.GetEnvironmentVariable("XDG_SESSION_TYPE"))
            .Returns((string?)null);
        _environmentMock.Setup(e => e.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            .Returns("wayland-0");

        var factory = new TextTyperFactory(_loggerFactoryMock.Object, _environmentMock.Object);

        // Act
        var result = factory.IsWayland();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsWayland_WithDisplayOnly_ShouldReturnFalse()
    {
        // Arrange
        _environmentMock.Setup(e => e.GetEnvironmentVariable("XDG_SESSION_TYPE"))
            .Returns((string?)null);
        _environmentMock.Setup(e => e.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            .Returns((string?)null);
        _environmentMock.Setup(e => e.GetEnvironmentVariable("DISPLAY"))
            .Returns(":0");

        var factory = new TextTyperFactory(_loggerFactoryMock.Object, _environmentMock.Object);

        // Act
        var result = factory.IsWayland();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsWayland_WithNoEnvironmentVariables_ShouldReturnTrue()
    {
        // Arrange - default to Wayland for modern systems
        _environmentMock.Setup(e => e.GetEnvironmentVariable(It.IsAny<string>()))
            .Returns((string?)null);

        var factory = new TextTyperFactory(_loggerFactoryMock.Object, _environmentMock.Object);

        // Act
        var result = factory.IsWayland();

        // Assert
        Assert.True(result); // Default to Wayland
    }

    [Theory]
    [InlineData("wayland", "wayland")]
    [InlineData("x11", "x11")]
    [InlineData("tty", "tty")]
    public void GetDisplayServerName_WithXdgSessionType_ShouldReturnSessionType(
        string sessionType, string expected)
    {
        // Arrange
        _environmentMock.Setup(e => e.GetEnvironmentVariable("XDG_SESSION_TYPE"))
            .Returns(sessionType);

        var factory = new TextTyperFactory(_loggerFactoryMock.Object, _environmentMock.Object);

        // Act
        var result = factory.GetDisplayServerName();

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetDisplayServerName_WithWaylandDisplay_ShouldReturnWayland()
    {
        // Arrange
        _environmentMock.Setup(e => e.GetEnvironmentVariable("XDG_SESSION_TYPE"))
            .Returns((string?)null);
        _environmentMock.Setup(e => e.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            .Returns("wayland-0");

        var factory = new TextTyperFactory(_loggerFactoryMock.Object, _environmentMock.Object);

        // Act
        var result = factory.GetDisplayServerName();

        // Assert
        Assert.Equal("wayland", result);
    }

    [Fact]
    public void GetDisplayServerName_WithDisplayOnly_ShouldReturnX11()
    {
        // Arrange
        _environmentMock.Setup(e => e.GetEnvironmentVariable("XDG_SESSION_TYPE"))
            .Returns((string?)null);
        _environmentMock.Setup(e => e.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            .Returns((string?)null);
        _environmentMock.Setup(e => e.GetEnvironmentVariable("DISPLAY"))
            .Returns(":0");

        var factory = new TextTyperFactory(_loggerFactoryMock.Object, _environmentMock.Object);

        // Act
        var result = factory.GetDisplayServerName();

        // Assert
        Assert.Equal("x11", result);
    }

    [Fact]
    public void GetDisplayServerName_WithNoEnvironmentVariables_ShouldReturnUnknown()
    {
        // Arrange
        _environmentMock.Setup(e => e.GetEnvironmentVariable(It.IsAny<string>()))
            .Returns((string?)null);

        var factory = new TextTyperFactory(_loggerFactoryMock.Object, _environmentMock.Object);

        // Act
        var result = factory.GetDisplayServerName();

        // Assert
        Assert.Equal("unknown", result);
    }

    // COMMENTED OUT: This test creates real ITextTyper instance which spawns GUI processes
    // [SkipOnCIFact]
    // public void Create_ShouldReturnITextTyperInstance()
    // {
    //     // Arrange - setup for Wayland environment
    //     _environmentMock.Setup(e => e.GetEnvironmentVariable("XDG_SESSION_TYPE"))
    //         .Returns("wayland");
    //
    //     var factory = new TextTyperFactory(_loggerFactoryMock.Object, _environmentMock.Object);
    //
    //     // Act
    //     var result = factory.Create();
    //
    //     // Assert
    //     Assert.NotNull(result);
    //     Assert.IsAssignableFrom<ITextTyper>(result);
    // }
}

public class SystemEnvironmentProviderTests
{
    [Fact]
    public void GetEnvironmentVariable_ShouldReturnSystemEnvironmentValue()
    {
        // Arrange
        var provider = new SystemEnvironmentProvider();
        var testVarName = "PATH"; // PATH should always be set

        // Act
        var result = provider.GetEnvironmentVariable(testVarName);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetEnvironmentVariable_WithNonExistentVariable_ShouldReturnNull()
    {
        // Arrange
        var provider = new SystemEnvironmentProvider();
        var testVarName = "SPEECHTOTEXT_TEST_NONEXISTENT_VAR_12345";

        // Act
        var result = provider.GetEnvironmentVariable(testVarName);

        // Assert
        Assert.Null(result);
    }
}
