# AntiBridge.Avalonia

A simplified cross-platform desktop app for managing Antigravity accounts with OAuth login and quota monitoring.

## Features

- 🔐 **Google OAuth Login** - One-click login with your Google account
- 📊 **Quota Dashboard** - Real-time view of AI model quota percentages
- 🎨 **Modern Dark UI** - Sleek Avalonia-based interface

## Requirements

- .NET 8.0 SDK
- Linux/Windows/macOS

## Build & Run

```bash
cd /home/lthieu1-ub/Documents/AntiBridge.Avalonia
dotnet restore
dotnet build
dotnet run --project src/AntiBridge.Avalonia
```

## Project Structure

```
src/
├── AntiBridge.Core/           # Business logic
│   ├── Models/                # Account, Token, Quota models
│   └── Services/              # OAuth and Quota services
└── AntiBridge.Avalonia/       # UI layer
    ├── ViewModels/            # MVVM ViewModels
    └── Views/                 # AXAML views
```

## Credits

Logic ported from [Antigravity-Manager](https://github.com/lbjlaq/Antigravity-Manager) (Tauri/Rust).
