# SpeechToText

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux-FCC624)](https://www.linux.org/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Linux push-to-talk dictation. CapsLock triggers audio capture, Whisper transcribes, xdotool/dotool types result.

## Features

- Push-to-talk via CapsLock (configurable)
- Whisper.net with CUDA GPU acceleration
- D-Bus system tray with animated icons
- Multi-trigger: keyboard, BT mouse, USB mouse
- X11 (xdotool) and Wayland (dotool) support
- SignalR hub for integrations

## Quick Start

```bash
git clone https://github.com/Olbrasoft/SpeechToText.git
cd SpeechToText
dotnet build
dotnet test
dotnet run --project src/SpeechToText.Service
```

## Requirements

| Requirement | Notes |
|-------------|-------|
| .NET 10 | Required |
| Linux | Debian 13+, ALSA |
| NVIDIA GPU | Optional (CUDA) |
| Whisper Model | [GGML format](https://huggingface.co/ggerganov/whisper.cpp) |

```bash
# Debian/Ubuntu
sudo apt install alsa-utils xdotool libgtk-3-dev libayatana-appindicator3-dev
# Wayland: sudo apt install dotool
```

## Architecture

```
Presentation: SpeechToText.App, SpeechToText.Service
Core:         SpeechToText.Core (interfaces, models)
Infra:        SpeechToText (ALSA, evdev, Whisper, GTK)
```

**Note:** Project names are `SpeechToText.*`, namespaces are `Olbrasoft.SpeechToText.*`.

## Projects

| Project | Purpose |
|---------|---------|
| `SpeechToText.Core` | Platform-agnostic interfaces and models |
| `SpeechToText` | Linux implementations (ALSA, evdev, Whisper) |
| `SpeechToText.App` | Desktop app with tray icon |
| `SpeechToText.Service` | ASP.NET Core service + SignalR |

## Configuration

`appsettings.json`:

```json
{
  "PushToTalkDictation": {
    "KeyboardDevice": "/dev/input/by-id/...",
    "TriggerKey": "CapsLock",
    "GgmlModelPath": "/path/to/ggml-large-v3.bin",
    "WhisperLanguage": "cs"
  }
}
```

## SignalR Hub

Endpoint: `/hubs/ptt`

Events: `RecordingStarted`, `RecordingStopped`, `TranscriptionComplete(text)`, `Error(message)`

## Tests

39 unit tests (xUnit + Moq) in 4 test projects.

```bash
dotnet test
```

## Documentation

[Wiki](https://github.com/Olbrasoft/SpeechToText/wiki)

## License

MIT
