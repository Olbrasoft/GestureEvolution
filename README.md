# SpeechToText

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux-FCC624)](https://www.linux.org/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**Linux Voice-Controlled Push-to-Talk Dictation System**

A .NET 10 application for real-time speech-to-text transcription with system tray integration, designed for Linux desktop environments (GNOME, KDE).

## Features

- **Push-to-Talk Recording** - Hold CapsLock to record, release to transcribe
- **Whisper AI Transcription** - Uses Whisper.net with CUDA GPU acceleration
- **System Tray Integration** - D-Bus StatusNotifierItem with animated icons
- **Multi-Input Support** - Keyboard, Bluetooth mouse, USB mouse triggers
- **Display Server Support** - Works on both X11 (xdotool) and Wayland (dotool)
- **SignalR Hub** - Real-time notifications for external integrations
- **Text Filters** - Post-processing with regex replacements

## Quick Start

```bash
# Clone
git clone https://github.com/Olbrasoft/SpeechToText.git
cd SpeechToText

# Build
dotnet build

# Run tests
dotnet test

# Run service
dotnet run --project src/Olbrasoft.SpeechToText.Service
```

## Requirements

| Requirement | Version | Notes |
|-------------|---------|-------|
| .NET SDK | 10.0 | Required |
| Linux | Debian 13+ | ALSA audio support |
| NVIDIA GPU | Optional | For CUDA acceleration |
| Whisper Model | GGML format | [Download models](https://huggingface.co/ggerganov/whisper.cpp) |

### System Packages

```bash
# Debian/Ubuntu
sudo apt install alsa-utils xdotool libgtk-3-dev libayatana-appindicator3-dev

# For Wayland
sudo apt install dotool
```

## Architecture

The project follows **Clean Architecture** with SOLID principles:

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                        │
│  ┌─────────────────────┐  ┌─────────────────────────────┐   │
│  │ SpeechToText.App    │  │ SpeechToText.Service        │   │
│  │ (Desktop App)       │  │ (ASP.NET Core + SignalR)    │   │
│  └─────────────────────┘  └─────────────────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│                    Core Layer                                │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ SpeechToText.Core                                   │    │
│  │ (Interfaces, Models - Platform Agnostic)            │    │
│  └─────────────────────────────────────────────────────┘    │
├─────────────────────────────────────────────────────────────┤
│                    Infrastructure Layer                      │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ SpeechToText (Linux Implementations)                │    │
│  │ ALSA, evdev, Whisper.net, GTK Interop               │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

## Project Structure

| Project | Description |
|---------|-------------|
| `Olbrasoft.SpeechToText.Core` | Platform-agnostic interfaces and models |
| `Olbrasoft.SpeechToText` | Linux implementations (ALSA, evdev, Whisper) |
| `Olbrasoft.SpeechToText.App` | Standalone desktop application |
| `Olbrasoft.SpeechToText.Service` | ASP.NET Core service with SignalR |
| `Olbrasoft.SpeechToText.Tests` | Unit tests (xUnit + Moq) |

## Configuration

Edit `appsettings.json`:

```json
{
  "PushToTalkDictation": {
    "KeyboardDevice": "/dev/input/by-id/usb-...-event-kbd",
    "TriggerKey": "CapsLock",
    "GgmlModelPath": "/path/to/ggml-large-v3.bin",
    "WhisperLanguage": "cs"
  }
}
```

See [Configuration Wiki](https://github.com/Olbrasoft/SpeechToText/wiki/Configuration) for all options.

## Usage

### As a Service

```bash
# Run directly
dotnet run --project src/Olbrasoft.SpeechToText.Service

# Or install as systemd service
systemctl --user enable speech-to-text.service
systemctl --user start speech-to-text.service
```

### How It Works

1. **Hold CapsLock** → Recording starts, tray icon animates
2. **Speak** → Audio captured via ALSA
3. **Release CapsLock** → Audio sent to Whisper for transcription
4. **Text typed** → Transcribed text inserted into active window

## API

### SignalR Hub

Connect to `/hubs/ptt` for real-time events:

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:5055/hubs/ptt")
    .build();

connection.on("RecordingStarted", () => console.log("Recording..."));
connection.on("TranscriptionComplete", (text) => console.log(text));
```

## Development

```bash
# Run tests
dotnet test

# Build release
dotnet publish -c Release

# Check code
dotnet build --warnaserror
```

See [Development Guide](https://github.com/Olbrasoft/SpeechToText/wiki/Development-Guide) for more details.

## Documentation

📖 **[Wiki Documentation](https://github.com/Olbrasoft/SpeechToText/wiki)**

- [Architecture](https://github.com/Olbrasoft/SpeechToText/wiki/Architecture) - Design patterns and data flow
- [Project Structure](https://github.com/Olbrasoft/SpeechToText/wiki/Project-Structure) - Code organization
- [API Reference](https://github.com/Olbrasoft/SpeechToText/wiki/API-Reference) - Interfaces and models
- [Configuration](https://github.com/Olbrasoft/SpeechToText/wiki/Configuration) - All options explained
- [Development Guide](https://github.com/Olbrasoft/SpeechToText/wiki/Development-Guide) - Contributing

## Project Status

| Aspect | Status |
|--------|--------|
| Core Functionality | ✅ Production Ready |
| Architecture | ✅ Clean Architecture (Phase 1) |
| Test Coverage | ✅ 39 unit tests |
| Platform | 🐧 Linux only |

## License

MIT License - see [LICENSE](LICENSE) for details.

---

**[Olbrasoft](https://github.com/Olbrasoft)** | Linux Voice-Controlled Dictation
