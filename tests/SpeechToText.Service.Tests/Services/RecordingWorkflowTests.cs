using Moq;
using Microsoft.Extensions.Logging;
using Olbrasoft.SpeechToText.Core.Interfaces;
using Olbrasoft.SpeechToText.Service.Services;

namespace SpeechToText.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="RecordingWorkflow"/>.
/// </summary>
public class RecordingWorkflowTests
{
    private readonly Mock<ILogger<RecordingWorkflow>> _loggerMock;
    private readonly Mock<IAudioRecorder> _audioRecorderMock;
    private readonly Mock<ITranscriptionProcessor> _transcriptionProcessorMock;
    private readonly Mock<ITextOutputService> _textOutputServiceMock;
    private readonly Mock<IPttNotifier> _pttNotifierMock;
    private readonly Mock<IRecordingModeManager> _recordingModeManagerMock;
    private readonly RecordingWorkflow _sut;

    public RecordingWorkflowTests()
    {
        _loggerMock = new Mock<ILogger<RecordingWorkflow>>();
        _audioRecorderMock = new Mock<IAudioRecorder>();
        _transcriptionProcessorMock = new Mock<ITranscriptionProcessor>();
        _textOutputServiceMock = new Mock<ITextOutputService>();
        _pttNotifierMock = new Mock<IPttNotifier>();
        _recordingModeManagerMock = new Mock<IRecordingModeManager>();

        _sut = new RecordingWorkflow(
            _loggerMock.Object,
            _audioRecorderMock.Object,
            _transcriptionProcessorMock.Object,
            _textOutputServiceMock.Object,
            _pttNotifierMock.Object,
            _recordingModeManagerMock.Object);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordingWorkflow(
            null!,
            _audioRecorderMock.Object,
            _transcriptionProcessorMock.Object,
            _textOutputServiceMock.Object,
            _pttNotifierMock.Object,
            _recordingModeManagerMock.Object));
    }

    [Fact]
    public void Constructor_NullAudioRecorder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordingWorkflow(
            _loggerMock.Object,
            null!,
            _transcriptionProcessorMock.Object,
            _textOutputServiceMock.Object,
            _pttNotifierMock.Object,
            _recordingModeManagerMock.Object));
    }

    [Fact]
    public void Constructor_NullTranscriptionProcessor_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordingWorkflow(
            _loggerMock.Object,
            _audioRecorderMock.Object,
            null!,
            _textOutputServiceMock.Object,
            _pttNotifierMock.Object,
            _recordingModeManagerMock.Object));
    }

    [Fact]
    public void Constructor_NullTextOutputService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordingWorkflow(
            _loggerMock.Object,
            _audioRecorderMock.Object,
            _transcriptionProcessorMock.Object,
            null!,
            _pttNotifierMock.Object,
            _recordingModeManagerMock.Object));
    }

    [Fact]
    public void Constructor_NullPttNotifier_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordingWorkflow(
            _loggerMock.Object,
            _audioRecorderMock.Object,
            _transcriptionProcessorMock.Object,
            _textOutputServiceMock.Object,
            null!,
            _recordingModeManagerMock.Object));
    }

    [Fact]
    public void Constructor_NullRecordingModeManager_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordingWorkflow(
            _loggerMock.Object,
            _audioRecorderMock.Object,
            _transcriptionProcessorMock.Object,
            _textOutputServiceMock.Object,
            _pttNotifierMock.Object,
            null!));
    }

    [Fact]
    public void IsRecording_InitialState_ReturnsFalse()
    {
        Assert.False(_sut.IsRecording);
    }

    [Fact]
    public void RecordingStartTime_InitialState_ReturnsNull()
    {
        Assert.Null(_sut.RecordingStartTime);
    }

    [Fact]
    public async Task StartRecordingAsync_WhenNotRecording_StartsRecording()
    {
        // Arrange
        _recordingModeManagerMock.Setup(x => x.EnterRecordingModeAsync())
            .ReturnsAsync(new RecordingModeContext(false));

        // Act
        await _sut.StartRecordingAsync();

        // Assert
        Assert.True(_sut.IsRecording);
        Assert.NotNull(_sut.RecordingStartTime);
        _pttNotifierMock.Verify(x => x.NotifyRecordingStartedAsync(), Times.Once);
        _audioRecorderMock.Verify(x => x.StartRecordingAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartRecordingAsync_WhenAlreadyRecording_DoesNotStartAgain()
    {
        // Arrange
        _recordingModeManagerMock.Setup(x => x.EnterRecordingModeAsync())
            .ReturnsAsync(new RecordingModeContext(false));

        await _sut.StartRecordingAsync();
        _pttNotifierMock.Invocations.Clear();
        _audioRecorderMock.Invocations.Clear();

        // Act
        await _sut.StartRecordingAsync();

        // Assert
        _pttNotifierMock.Verify(x => x.NotifyRecordingStartedAsync(), Times.Never);
        _audioRecorderMock.Verify(x => x.StartRecordingAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopAndProcessAsync_WhenNotRecording_ReturnsFailure()
    {
        // Act
        var result = await _sut.StopAndProcessAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal("No recording in progress", result.ErrorMessage);
    }

    [Fact]
    public async Task StopAndProcessAsync_WithNoAudioData_ReturnsFailure()
    {
        // Arrange
        _recordingModeManagerMock.Setup(x => x.EnterRecordingModeAsync())
            .ReturnsAsync(new RecordingModeContext(false));
        _audioRecorderMock.Setup(x => x.GetRecordedData()).Returns(Array.Empty<byte>());

        await _sut.StartRecordingAsync();

        // Act
        var result = await _sut.StopAndProcessAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal("No audio data recorded", result.ErrorMessage);
        Assert.False(_sut.IsRecording);
        // Note: PlayRejectionSoundAsync is called via fire-and-forget, so we don't verify it here
    }

    [Fact]
    public async Task StopAndProcessAsync_WithSuccessfulTranscription_ReturnsSuccess()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3 };
        var transcribedText = "Hello world";

        _recordingModeManagerMock.Setup(x => x.EnterRecordingModeAsync())
            .ReturnsAsync(new RecordingModeContext(false));
        _audioRecorderMock.Setup(x => x.GetRecordedData()).Returns(audioData);
        _transcriptionProcessorMock.Setup(x => x.ProcessAsync(audioData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionProcessorResult(true, transcribedText, 0.95f, false, null));

        await _sut.StartRecordingAsync();

        // Act
        var result = await _sut.StopAndProcessAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(transcribedText, result.TranscribedText);
        Assert.False(_sut.IsRecording);
        _textOutputServiceMock.Verify(x => x.OutputTextAsync(transcribedText), Times.Once);
        _pttNotifierMock.Verify(x => x.NotifyTranscriptionCompletedAsync(transcribedText, It.IsAny<float>()), Times.Once);
    }

    [Fact]
    public async Task StopAndProcessAsync_WhenHallucination_ReturnsFailure()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3 };

        _recordingModeManagerMock.Setup(x => x.EnterRecordingModeAsync())
            .ReturnsAsync(new RecordingModeContext(false));
        _audioRecorderMock.Setup(x => x.GetRecordedData()).Returns(audioData);
        _transcriptionProcessorMock.Setup(x => x.ProcessAsync(audioData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionProcessorResult(false, null, 0f, true, "Whisper hallucination filtered"));

        await _sut.StartRecordingAsync();

        // Act
        var result = await _sut.StopAndProcessAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("hallucination", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        // Note: PlayRejectionSoundAsync is called via fire-and-forget, so we don't verify it here
    }

    [Fact]
    public async Task StopAndProcessAsync_WhenTranscriptionFails_ReturnsFailure()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3 };

        _recordingModeManagerMock.Setup(x => x.EnterRecordingModeAsync())
            .ReturnsAsync(new RecordingModeContext(false));
        _audioRecorderMock.Setup(x => x.GetRecordedData()).Returns(audioData);
        _transcriptionProcessorMock.Setup(x => x.ProcessAsync(audioData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionProcessorResult(false, null, 0f, false, "Transcription failed"));

        await _sut.StartRecordingAsync();

        // Act
        var result = await _sut.StopAndProcessAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Transcription failed", result.ErrorMessage);
    }

    [Fact]
    public async Task StopAndProcessAsync_WhenCancelled_ReturnsCancelled()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3 };
        var cts = new CancellationTokenSource();

        _recordingModeManagerMock.Setup(x => x.EnterRecordingModeAsync())
            .ReturnsAsync(new RecordingModeContext(false));
        _audioRecorderMock.Setup(x => x.GetRecordedData()).Returns(audioData);
        _transcriptionProcessorMock.Setup(x => x.ProcessAsync(audioData, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await _sut.StartRecordingAsync();

        // Act
        var result = await _sut.StopAndProcessAsync(cts.Token);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.WasCancelled);
        // Note: PlayRejectionSoundAsync is called via fire-and-forget, so we don't verify it here
    }

    [Fact]
    public async Task StopAndProcessAsync_ResetsStateAfterCompletion()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3 };

        _recordingModeManagerMock.Setup(x => x.EnterRecordingModeAsync())
            .ReturnsAsync(new RecordingModeContext(false));
        _audioRecorderMock.Setup(x => x.GetRecordedData()).Returns(audioData);
        _transcriptionProcessorMock.Setup(x => x.ProcessAsync(audioData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionProcessorResult(true, "Test", 0.9f, false, null));

        await _sut.StartRecordingAsync();

        // Act
        await _sut.StopAndProcessAsync();

        // Assert
        Assert.False(_sut.IsRecording);
        Assert.Null(_sut.RecordingStartTime);
    }

    [Fact]
    public async Task StopAndProcessAsync_ExitsRecordingMode()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3 };
        var context = new RecordingModeContext(false);

        _recordingModeManagerMock.Setup(x => x.EnterRecordingModeAsync())
            .ReturnsAsync(context);
        _audioRecorderMock.Setup(x => x.GetRecordedData()).Returns(audioData);
        _transcriptionProcessorMock.Setup(x => x.ProcessAsync(audioData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionProcessorResult(true, "Test", 0.9f, false, null));

        await _sut.StartRecordingAsync();

        // Act
        await _sut.StopAndProcessAsync();

        // Assert
        _recordingModeManagerMock.Verify(x => x.ExitRecordingModeAsync(context), Times.Once);
    }

    [Fact]
    public void CancelTranscription_WhenNoTranscriptionInProgress_DoesNothing()
    {
        // Act & Assert - should not throw
        _sut.CancelTranscription();
    }
}
