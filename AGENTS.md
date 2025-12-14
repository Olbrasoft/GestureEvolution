# SpeechToText

Linux PTT dictation. CapsLock→record→Whisper→type.

## Stack
.NET10 | ASP.NET Core | SignalR | Whisper.net(CUDA) | ALSA | evdev | D-Bus

## Structure
```
src/SpeechToText.Core/       # Interfaces, models
src/SpeechToText/            # Linux impls
src/SpeechToText.App/        # Desktop+tray
src/SpeechToText.Service/    # ASP.NET+SignalR
tests/*Tests/                # xUnit+Moq (39 tests)
```
Projects=`SpeechToText.*` Namespaces=`Olbrasoft.SpeechToText.*`

## Key Files
Core/Interfaces: IAudioRecorder, IKeyboardMonitor(read), IKeySimulator(write-ISP), ISpeechTranscriber, ITextTyper
Core/Models: AudioDataEventArgs, KeyCode, KeyEventArgs, TranscriptionResult
Impls: AlsaAudioRecorder, EvdevKeyboardMonitor, UinputKeySimulator, WhisperNetTranscriber
TextInput: XdotoolTextTyper(X11), DotoolTextTyper(Wayland), TextTyperFactory
App: DictationService, DBusTrayIcon, SingleInstanceLock
Service: DictationWorker, PttHub

## Commands
```bash
dotnet build && dotnet test
dotnet run --project src/SpeechToText.Service
```

## Config (appsettings.json)
```json
{"PushToTalkDictation":{"KeyboardDevice":"/dev/input/by-id/...","TriggerKey":"CapsLock","GgmlModelPath":"/path/ggml-large-v3.bin","WhisperLanguage":"cs"}}
```

## Flow
CapsLock↓→EvdevMonitor→Dictation→AlsaRecorder+TrayAnim
CapsLock↑→StopRecord→Whisper→Filter→Typer→PttHub

## Patterns
CleanArch | ISP(Monitor/Simulator) | Strategy(TextTyper) | DI | IAsyncDisposable

## Deps
Whisper.net | Whisper.net.Runtime.Cuda.Linux | NAudio | SkiaSharp | Tmds.DBus

## Notes
- evdev needs root/input group
- Device: `/dev/input/by-id/`
- Lock: `/tmp/push-to-talk-dictation.lock`
- CUDA optional→CPU fallback

## API
SignalR `/hubs/ptt`: RecordingStarted, RecordingStopped, TranscriptionComplete(text), Error(msg)
