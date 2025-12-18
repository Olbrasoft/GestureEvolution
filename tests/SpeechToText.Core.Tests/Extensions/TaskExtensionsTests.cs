using Microsoft.Extensions.Logging;
using Moq;
using Olbrasoft.SpeechToText.Core.Extensions;
using SttTaskExtensions = Olbrasoft.SpeechToText.Core.Extensions.TaskExtensions;

namespace SpeechToText.Core.Tests.Extensions;

public class TaskExtensionsTests
{
    private readonly Mock<ILogger> _loggerMock;

    public TaskExtensionsTests()
    {
        _loggerMock = new Mock<ILogger>();
        _loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
    }

    [Fact]
    public async Task FireAndForget_SuccessfulTask_DoesNotLogError()
    {
        // Arrange
        var task = Task.CompletedTask;

        // Act
        task.FireAndForget(_loggerMock.Object, "TestContext");
        await Task.Delay(50); // Allow continuation to run

        // Assert
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task FireAndForget_FailedTask_LogsError()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        var task = Task.FromException(exception);

        // Act
        task.FireAndForget(_loggerMock.Object, "TestContext");
        await Task.Delay(50); // Allow continuation to run

        // Assert
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("TestContext")),
                It.IsAny<AggregateException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task FireAndForget_WithoutContext_LogsGenericMessage()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        var task = Task.FromException(exception);

        // Act
        task.FireAndForget(_loggerMock.Object);
        await Task.Delay(50); // Allow continuation to run

        // Assert
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("fire-and-forget")),
                It.IsAny<AggregateException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task FireAndForget_DelayedFailure_LogsErrorWhenFails()
    {
        // Arrange
        var task = Task.Run(async () =>
        {
            await Task.Delay(10);
            throw new InvalidOperationException("Delayed failure");
        });

        // Act
        task.FireAndForget(_loggerMock.Object, "DelayedTask");
        await Task.Delay(100); // Allow task to fail and continuation to run

        // Assert
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DelayedTask")),
                It.IsAny<AggregateException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void RunFireAndForget_Action_ExecutesSuccessfully()
    {
        // Arrange
        var executed = false;

        // Act
        SttTaskExtensions.RunFireAndForget(() => executed = true, _loggerMock.Object, "ActionTest");

        // Assert - wait for execution
        Thread.Sleep(50);
        Assert.True(executed);
    }

    [Fact]
    public async Task RunFireAndForget_AsyncAction_ExecutesSuccessfully()
    {
        // Arrange
        var executed = false;

        // Act
        SttTaskExtensions.RunFireAndForget(async () =>
        {
            await Task.Delay(10);
            executed = true;
        }, _loggerMock.Object, "AsyncActionTest");

        // Assert
        await Task.Delay(100);
        Assert.True(executed);
    }

    [Fact]
    public async Task RunFireAndForget_FailingAction_LogsError()
    {
        // Arrange & Act
        SttTaskExtensions.RunFireAndForget(
            () => throw new InvalidOperationException("Action failed"),
            _loggerMock.Object,
            "FailingAction");

        await Task.Delay(50);

        // Assert
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("FailingAction")),
                It.IsAny<AggregateException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RunFireAndForget_FailingAsyncAction_LogsError()
    {
        // Arrange & Act
        SttTaskExtensions.RunFireAndForget(
            async () =>
            {
                await Task.Delay(10);
                throw new InvalidOperationException("Async action failed");
            },
            _loggerMock.Object,
            "FailingAsyncAction");

        await Task.Delay(100);

        // Assert
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("FailingAsyncAction")),
                It.IsAny<AggregateException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
