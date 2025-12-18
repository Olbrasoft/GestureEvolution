// PushToTalk Remote Control
// Connects to SignalR hub and provides remote recording control

const elements = {
    connectionStatus: document.getElementById('connectionStatus'),
    statusDot: document.getElementById('statusDot'),
    statusText: document.getElementById('statusText'),
    duration: document.getElementById('duration'),
    btnToggle: document.getElementById('btnToggle'),
    toggleIcon: document.getElementById('toggleIcon'),
    toggleText: document.getElementById('toggleText'),
    btnEnter: document.getElementById('btnEnter'),
    transcriptionText: document.getElementById('transcriptionText')
};

let connection = null;
let isRecording = false;
let isTranscribing = false;
let durationInterval = null;
let recordingStartTime = null;

// Build SignalR connection
function buildConnection() {
    // Use relative URL - works on any host
    const hubUrl = window.location.origin + '/hubs/dictation';

    connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Information)
        .build();

    // Connection lifecycle events
    connection.onreconnecting((error) => {
        console.log('Reconnecting...', error);
        setConnectionStatus(false);
    });

    connection.onreconnected((connectionId) => {
        console.log('Reconnected:', connectionId);
        setConnectionStatus(true);
        refreshStatus();
    });

    connection.onclose((error) => {
        console.log('Connection closed:', error);
        setConnectionStatus(false);
    });

    // Dictation Events from server
    connection.on('DictationEvent', handleDictationEvent);
    connection.on('Connected', (connectionId) => {
        console.log('Connected with ID:', connectionId);
    });
}

function handleDictationEvent(event) {
    console.log('DictationEvent:', event);

    switch (event.eventType) {
        case 0: // RecordingStarted
            setRecordingState(true, false);
            break;

        case 1: // RecordingStopped
            setRecordingState(false, false);
            break;

        case 2: // TranscriptionStarted
            setRecordingState(false, true);
            break;

        case 3: // TranscriptionCompleted
            setRecordingState(false, false);
            if (event.text) {
                setTranscriptionText(event.text);
            }
            break;

        case 4: // TranscriptionFailed
            setRecordingState(false, false);
            break;
    }
}

function setConnectionStatus(connected) {
    elements.connectionStatus.textContent = connected ? 'Pripojeno' : 'Odpojeno';
    elements.connectionStatus.className = 'connection-status ' + (connected ? 'connected' : 'disconnected');

    elements.btnToggle.disabled = !connected;
    elements.btnEnter.disabled = !connected;

    if (!connected) {
        elements.statusDot.className = 'status-dot';
        elements.statusText.textContent = 'Odpojeno';
    }
}

function setRecordingState(recording, transcribing) {
    isRecording = recording;
    isTranscribing = transcribing;

    // Update status dot
    elements.statusDot.className = 'status-dot connected';
    if (recording) {
        elements.statusDot.classList.add('recording');
        elements.statusText.textContent = 'Nahrava se...';
    } else if (transcribing) {
        elements.statusDot.classList.add('transcribing');
        elements.statusText.textContent = 'Prepisuje se...';
    } else {
        elements.statusText.textContent = 'Pripraveno';
    }

    // Update toggle button - three states: Start, Stop, Zrusit
    elements.btnToggle.classList.remove('recording', 'transcribing');
    if (recording) {
        elements.btnToggle.classList.add('recording');
        elements.btnToggle.disabled = false;
        elements.toggleIcon.innerHTML = '&#9632;'; // Stop icon
        elements.toggleText.textContent = 'Stop';
    } else if (transcribing) {
        elements.btnToggle.classList.add('transcribing');
        elements.btnToggle.disabled = false; // ENABLED - clicking cancels transcription
        elements.toggleIcon.innerHTML = '&#10006;'; // X icon for cancel
        elements.toggleText.textContent = 'Zrusit';
    } else {
        elements.btnToggle.disabled = false;
        elements.toggleIcon.innerHTML = '&#9658;'; // Play icon
        elements.toggleText.textContent = 'Start';
    }

    // Update duration display
    if (recording) {
        recordingStartTime = Date.now();
        elements.duration.classList.add('visible');
        startDurationTimer();
    } else {
        stopDurationTimer();
        if (!transcribing) {
            elements.duration.classList.remove('visible');
        }
    }
}

function setTranscriptionText(text) {
    elements.transcriptionText.textContent = text;
    elements.transcriptionText.classList.remove('empty');
}

function startDurationTimer() {
    stopDurationTimer();
    durationInterval = setInterval(() => {
        if (recordingStartTime) {
            const elapsed = Math.floor((Date.now() - recordingStartTime) / 1000);
            const minutes = Math.floor(elapsed / 60).toString().padStart(2, '0');
            const seconds = (elapsed % 60).toString().padStart(2, '0');
            elements.duration.textContent = `${minutes}:${seconds}`;
        }
    }, 100);
}

function stopDurationTimer() {
    if (durationInterval) {
        clearInterval(durationInterval);
        durationInterval = null;
    }
}

async function refreshStatus() {
    try {
        const status = await connection.invoke('GetStatus');
        console.log('Status:', status);
        setRecordingState(status.isRecording, status.isTranscribing);
    } catch (error) {
        console.error('Failed to get status:', error);
    }
}

// Button handler - three functions: Start, Stop, Cancel
elements.btnToggle.addEventListener('click', async () => {
    console.log('Toggle button clicked, isRecording:', isRecording, 'isTranscribing:', isTranscribing);

    if (isTranscribing) {
        // During transcription, toggle button acts as Cancel
        console.log('Calling CancelTranscription...');
        try {
            await connection.invoke('CancelTranscription');
            console.log('CancelTranscription completed');
        } catch (error) {
            console.error('Cancel failed:', error);
        }
        return;
    }

    // Start or Stop recording
    try {
        await connection.invoke('ToggleRecording');
        console.log('ToggleRecording completed');
    } catch (error) {
        console.error('Toggle failed:', error);
    }
});

// Enter button - sends Enter key press
elements.btnEnter.addEventListener('click', async () => {
    console.log('Enter button clicked');
    try {
        await connection.invoke('SendEnter');
        console.log('SendEnter completed');
    } catch (error) {
        console.error('SendEnter failed:', error);
    }
});

// Initialize
async function initialize() {
    buildConnection();

    try {
        await connection.start();
        console.log('SignalR connected');
        setConnectionStatus(true);
        await refreshStatus();
    } catch (error) {
        console.error('Failed to connect:', error);
        setConnectionStatus(false);

        // Retry after 5 seconds
        setTimeout(initialize, 5000);
    }
}

// Start when page loads
initialize();
