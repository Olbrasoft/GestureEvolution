using Microsoft.Extensions.Logging;
using Moq;
using Olbrasoft.SpeechToText.App;
using Olbrasoft.SpeechToText.App.Services;
using Olbrasoft.SpeechToText.Core.Interfaces;
using Olbrasoft.SpeechToText.Core.Models;

namespace Olbrasoft.SpeechToText.App.Tests;

public class DictationServiceTests : IDisposable
{
    private readonly Mock<ILogger<DictationService>> _loggerMock;
    private readonly Mock<IKeyboardMonitor> _keyboardMonitorMock;
    private readonly Mock<IAudioRecorder> _audioRecorderMock;
    private readonly Mock<ITranscriptionCoordinator> _transcriptionCoordinatorMock;
    private readonly Mock<ITextOutputHandler> _textOutputHandlerMock;
    private readonly DictationService _service;

    public DictationServiceTests()
    {
        _loggerMock = new Mock<ILogger<DictationService>>();
        _keyboardMonitorMock = new Mock<IKeyboardMonitor>();
        _audioRecorderMock = new Mock<IAudioRecorder>();
        _transcriptionCoordinatorMock = new Mock<ITranscriptionCoordinator>();
        _textOutputHandlerMock = new Mock<ITextOutputHandler>();

        _service = new DictationService(
            _loggerMock.Object,
            _keyboardMonitorMock.Object,
            _audioRecorderMock.Object,
            _transcriptionCoordinatorMock.Object,
            _textOutputHandlerMock.Object);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public void InitialState_ShouldBeIdle()
    {
        // Assert
        Assert.Equal(DictationState.Idle, _service.State);
    }

    [Fact]
    public async Task StartDictationAsync_WhenIdle_ShouldStartRecording()
    {
        // Arrange
        var stateChanges = new List<DictationState>();
        _service.StateChanged += (_, state) => stateChanges.Add(state);

        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.StartDictationAsync();

        // Assert
        Assert.Equal(DictationState.Recording, _service.State);
        Assert.Contains(DictationState.Recording, stateChanges);
        _audioRecorderMock.Verify(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartDictationAsync_WhenNotIdle_ShouldNotStartRecording()
    {
        // Arrange - start first dictation
        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await _service.StartDictationAsync();

        // Act - try to start again while recording
        await _service.StartDictationAsync();

        // Assert
        _audioRecorderMock.Verify(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopDictationAsync_WhenNotRecording_ShouldDoNothing()
    {
        // Act
        await _service.StopDictationAsync();

        // Assert
        _audioRecorderMock.Verify(r => r.StopRecordingAsync(), Times.Never);
        Assert.Equal(DictationState.Idle, _service.State);
    }

    [Fact]
    public async Task StopDictationAsync_WithNoAudioData_ShouldReturnToIdle()
    {
        // Arrange
        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.StopRecordingAsync())
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.GetRecordedData())
            .Returns(Array.Empty<byte>());

        await _service.StartDictationAsync();

        // Act
        await _service.StopDictationAsync();

        // Assert
        Assert.Equal(DictationState.Idle, _service.State);
        _transcriptionCoordinatorMock.Verify(
            t => t.TranscribeWithFeedbackAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StopDictationAsync_WithAudioData_ShouldTranscribeAndOutput()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3, 4, 5 };
        var transcriptionResult = new TranscriptionResult("Hello World", 1.0f);

        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.StopRecordingAsync())
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.GetRecordedData())
            .Returns(audioData);
        _transcriptionCoordinatorMock.Setup(t => t.TranscribeWithFeedbackAsync(audioData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcriptionResult);
        _textOutputHandlerMock.Setup(t => t.OutputTextAsync(It.IsAny<string>()))
            .ReturnsAsync("Hello World");

        await _service.StartDictationAsync();

        // Act
        await _service.StopDictationAsync();

        // Assert
        Assert.Equal(DictationState.Idle, _service.State);
        _transcriptionCoordinatorMock.Verify(
            t => t.TranscribeWithFeedbackAsync(audioData, It.IsAny<CancellationToken>()),
            Times.Once);
        _textOutputHandlerMock.Verify(t => t.OutputTextAsync("Hello World"), Times.Once);
    }

    [Fact]
    public async Task StopDictationAsync_WithFailedTranscription_ShouldNotOutput()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3 };
        var transcriptionResult = new TranscriptionResult("Transcription failed");

        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.StopRecordingAsync())
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.GetRecordedData())
            .Returns(audioData);
        _transcriptionCoordinatorMock.Setup(t => t.TranscribeWithFeedbackAsync(audioData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcriptionResult);

        await _service.StartDictationAsync();

        // Act
        await _service.StopDictationAsync();

        // Assert
        Assert.Equal(DictationState.Idle, _service.State);
        _textOutputHandlerMock.Verify(t => t.OutputTextAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task StartMonitoringAsync_ShouldCallKeyboardMonitor()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately to prevent hanging

        _keyboardMonitorMock.Setup(k => k.StartMonitoringAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.StartMonitoringAsync(cts.Token);

        // Assert
        _keyboardMonitorMock.Verify(k => k.StartMonitoringAsync(cts.Token), Times.Once);
    }

    [Fact]
    public async Task StopMonitoringAsync_ShouldCallKeyboardMonitor()
    {
        // Arrange
        _keyboardMonitorMock.Setup(k => k.StopMonitoringAsync())
            .Returns(Task.CompletedTask);

        // Act
        await _service.StopMonitoringAsync();

        // Assert
        _keyboardMonitorMock.Verify(k => k.StopMonitoringAsync(), Times.Once);
    }

    [Fact]
    public async Task StateChanged_ShouldBeRaised_WhenStateChanges()
    {
        // Arrange
        var stateChanges = new List<DictationState>();
        _service.StateChanged += (_, state) => stateChanges.Add(state);

        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.StopRecordingAsync())
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.GetRecordedData())
            .Returns(Array.Empty<byte>());

        // Act
        await _service.StartDictationAsync();
        await _service.StopDictationAsync();

        // Assert
        Assert.Contains(DictationState.Recording, stateChanges);
        Assert.Contains(DictationState.Idle, stateChanges);
    }

    [Fact]
    public async Task StopDictationAsync_WithTranscription_ShouldGoThroughTranscribingState()
    {
        // Arrange
        var stateChanges = new List<DictationState>();
        _service.StateChanged += (_, state) => stateChanges.Add(state);

        var audioData = new byte[] { 1, 2, 3 };
        var transcriptionResult = new TranscriptionResult("Test", 1.0f);

        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.StopRecordingAsync())
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.GetRecordedData())
            .Returns(audioData);
        _transcriptionCoordinatorMock.Setup(t => t.TranscribeWithFeedbackAsync(audioData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcriptionResult);
        _textOutputHandlerMock.Setup(t => t.OutputTextAsync(It.IsAny<string>()))
            .ReturnsAsync("Test");

        await _service.StartDictationAsync();

        // Act
        await _service.StopDictationAsync();

        // Assert
        Assert.Contains(DictationState.Transcribing, stateChanges);
    }

    [Fact]
    public async Task StartDictationAsync_WhenRecordingFails_ShouldReturnToIdle()
    {
        // Arrange
        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Recording failed"));

        // Act
        await _service.StartDictationAsync();

        // Wait for the async ContinueWith error handler to execute
        await Task.Delay(50);

        // Assert
        Assert.Equal(DictationState.Idle, _service.State);
    }

    [Fact]
    public async Task StopDictationAsync_WhenTranscriptionFails_ShouldReturnToIdle()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3 };

        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.StopRecordingAsync())
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.GetRecordedData())
            .Returns(audioData);
        _transcriptionCoordinatorMock.Setup(t => t.TranscribeWithFeedbackAsync(audioData, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Transcription error"));

        await _service.StartDictationAsync();

        // Act
        await _service.StopDictationAsync();

        // Assert
        Assert.Equal(DictationState.Idle, _service.State);
    }

    [Fact]
    public async Task StopDictationAsync_WithEmptyText_ShouldNotOutput()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3 };
        var transcriptionResult = new TranscriptionResult("   ", 1.0f); // whitespace only

        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.StopRecordingAsync())
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.GetRecordedData())
            .Returns(audioData);
        _transcriptionCoordinatorMock.Setup(t => t.TranscribeWithFeedbackAsync(audioData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcriptionResult);

        await _service.StartDictationAsync();

        // Act
        await _service.StopDictationAsync();

        // Assert
        _textOutputHandlerMock.Verify(t => t.OutputTextAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Act & Assert - should not throw
        _service.Dispose();
    }

    [Fact]
    public void Dispose_MultipleCalls_ShouldNotThrow()
    {
        // Act & Assert
        _service.Dispose();
        _service.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_ShouldNotThrow()
    {
        // Act & Assert
        await _service.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_MultipleCalls_ShouldNotThrow()
    {
        // Act & Assert
        await _service.DisposeAsync();
        await _service.DisposeAsync();
    }

    [Fact]
    public async Task TranscriptionCompleted_ShouldBeRaised_WhenOutputSucceeds()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3 };
        var transcriptionResult = new TranscriptionResult("Hello", 1.0f);
        string? completedText = null;
        _service.TranscriptionCompleted += (_, text) => completedText = text;

        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.StopRecordingAsync())
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.GetRecordedData())
            .Returns(audioData);
        _transcriptionCoordinatorMock.Setup(t => t.TranscribeWithFeedbackAsync(audioData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcriptionResult);
        _textOutputHandlerMock.Setup(t => t.OutputTextAsync("Hello"))
            .ReturnsAsync("Hello");

        await _service.StartDictationAsync();

        // Act
        await _service.StopDictationAsync();

        // Assert
        Assert.Equal("Hello", completedText);
    }

    [Fact]
    public async Task TranscriptionCompleted_ShouldNotBeRaised_WhenOutputReturnsNull()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3 };
        var transcriptionResult = new TranscriptionResult("Test", 1.0f);
        bool eventRaised = false;
        _service.TranscriptionCompleted += (_, _) => eventRaised = true;

        _audioRecorderMock.Setup(r => r.StartRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.StopRecordingAsync())
            .Returns(Task.CompletedTask);
        _audioRecorderMock.Setup(r => r.GetRecordedData())
            .Returns(audioData);
        _transcriptionCoordinatorMock.Setup(t => t.TranscribeWithFeedbackAsync(audioData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcriptionResult);
        _textOutputHandlerMock.Setup(t => t.OutputTextAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null); // Output returns null (filtered out)

        await _service.StartDictationAsync();

        // Act
        await _service.StopDictationAsync();

        // Assert
        Assert.False(eventRaised);
    }
}

public class DictationStateTests
{
    [Fact]
    public void Idle_ShouldBeDefaultValue()
    {
        // Arrange
        DictationState defaultState = default;

        // Assert
        Assert.Equal(DictationState.Idle, defaultState);
    }

    [Theory]
    [InlineData(DictationState.Idle)]
    [InlineData(DictationState.Recording)]
    [InlineData(DictationState.Transcribing)]
    public void AllStates_ShouldBeValid(DictationState state)
    {
        // Assert
        Assert.True(Enum.IsDefined(state));
    }

    [Fact]
    public void AllStates_ShouldBeDistinct()
    {
        // Arrange
        var allStates = Enum.GetValues<DictationState>();

        // Act
        var distinctCount = allStates.Distinct().Count();

        // Assert
        Assert.Equal(allStates.Length, distinctCount);
    }
}
