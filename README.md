# RenOff (v0.1.0)

RenOff is a lightweight Windows app (WPF, .NET) for quick notes and to-dos, with reminders and “nudges” that help you review pending items.

This is the first public release (0.1.0). Feedback and bug reports are welcome.

## Features

- Notes and to-dos in a single list
- Item reminders (snooze / dismiss)
- Reading “nudge” reminders to review pending to-dos
- Runs in the system tray (close-to-tray supported)
- Offline-first: data stays local
- Themes: Light/Dark (Modern style)
- UI style: Classic/Modern (Classic disables themes)
- Languages: Italian / English
- Drag & drop reorder (persistent)
- Backup export/import (JSON)

## Privacy & data

- No sync, no account, no telemetry
- Local database:
  - `%LOCALAPPDATA%\RenOff\renoff.db`

## Backup / export

From the app Settings tab you can:
- Export a `.renoff.json` backup (items + reminders)
- Import a backup (replace all or merge)

## Download

Releases are distributed as a `.zip` file containing the published build.

For GitHub Releases you can publish:
- Framework-dependent (smaller): requires **.NET Desktop Runtime 8**
- Self-contained (bigger): includes the runtime (recommended for non-technical users)

## Build (dev)

Requirements: .NET SDK 8

```powershell
dotnet restore
dotnet build
dotnet run --project src/RenOff.App
```

## Publish (release)

### Option A: framework-dependent (win-x64)

```powershell
dotnet publish src/RenOff.App -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false
```

Output folder:
- `src\RenOff.App\bin\Release\net8.0-windows\win-x64\publish\`

Zip it, for example:
- `RenOff-v0.1.0-win-x64-framework-dependent.zip`

### Option B: self-contained (win-x64)

```powershell
dotnet publish src/RenOff.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false
```

Output folder:
- `src\RenOff.App\bin\Release\net8.0-windows\win-x64\publish\`

Zip it, for example:
- `RenOff-v0.1.0-win-x64-self-contained.zip`

## Creating a GitHub Release (manual)

1. Push the code to GitHub
2. On GitHub: **Releases** → **Draft a new release**
3. Tag: `v0.1.0`
4. Title: `RenOff 0.1.0`
5. Attach the zip produced by `dotnet publish`
6. Publish the release

## Feedback

Open a GitHub Issue with:
- app version (0.1.0)
- Windows version (10/11)
- expected vs actual behavior
- screenshots (if helpful)
