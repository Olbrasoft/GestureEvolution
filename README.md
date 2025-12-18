# PushToTalk

[![Auto Deploy](https://img.shields.io/badge/auto--deploy-enabled-green)](https://github.com/Olbrasoft/PushToTalk)

Push-to-Talk functionality for Linux voice assistant. Monitors mouse buttons and triggers speech-to-text recording.

## Getting Started

### Prerequisites

- .NET 10 SDK
- Linux (tested on Debian/Ubuntu)
- libevdev (for mouse button monitoring)

### Installation

```bash
git clone https://github.com/Olbrasoft/PushToTalk.git
cd PushToTalk
dotnet build
```

### Running Tests

```bash
dotnet test
```

## Project Structure

```
PushToTalk/
├── src/
│   ├── PushToTalk.Core/           # Core logic and interfaces
│   ├── PushToTalk.Linux/          # Linux-specific implementations
│   ├── PushToTalk.App/            # Desktop application
│   └── PushToTalk.Service/        # Background service
├── tests/
│   ├── PushToTalk.Core.Tests/
│   ├── PushToTalk.Linux.Tests/
│   ├── PushToTalk.App.Tests/
│   └── PushToTalk.Service.Tests/
├── assets/                        # Icons and resources
├── data/                          # Desktop/metainfo files
├── debian/                        # Debian packaging scripts
├── .github/workflows/             # CI/CD
└── PushToTalk.sln
```

## Architecture

- **Strategy Pattern** for different mouse button monitoring implementations
- **SOLID principles** throughout
- Clean separation between Core, Linux platform, App and Service layers

## License

MIT License - see [LICENSE](LICENSE) file.
