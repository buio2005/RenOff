# RenOff (v1.0.0)

RenOff is a lightweight Windows app (WPF, .NET) for quick notes and to-dos, with reminders and “nudges” that help you review pending items.

Version 1.0.0 is the first full stable release. Feedback and bug reports are welcome.

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
- Encrypted backup export/import
- App lock with password protection
- Recovery code support for lock reset

## Privacy & data

- No sync, no account, no telemetry
- Local database:
  - `%LOCALAPPDATA%\RenOff\renoff.db`

## Backup / export

From the app Settings tab you can:
- Export a `.renoff.json` backup (items + reminders)
- Export an encrypted `.renoff.enc` backup with passphrase protection
- Import a backup (replace all or merge)
- Import encrypted backups by entering the correct passphrase

## Security

- Optional app lock with password protection
- Recovery code shown once during password setup
- If the recovery code is used, app lock is disabled and timeout returns to `Never` for safety

## First run

On first launch, when the local database is empty, RenOff creates two sample items:
- `Prova: aggiungi un to-do`
- `Prova: nota rapida`

They are only starter examples to show how the app works, and can be edited or deleted at any time.

## Download

GitHub Releases provide two Windows assets:
- `RenOff-Setup-1.0.0.exe`: installer version
- `RenOff-v1.0.0-win-x64-self-contained.zip`: portable version

Recommended for most users:
- Use the installer for the simplest setup
- Use the portable zip if you prefer to extract and run the app manually

## Build (dev)

Requirements: .NET SDK 8

```powershell
dotnet restore
dotnet build
dotnet run --project src/RenOff.App
```

## Publish (release)

### Self-contained (win-x64)

```powershell
dotnet publish .\src\RenOff.App\RenOff.App.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish\win-x64
```

Output folder:
- `artifacts\publish\win-x64\`

Zip it, for example:
- `RenOff-v1.0.0-win-x64-self-contained.zip`

## Creating a GitHub Release (manual)

1. Push the code to GitHub
2. On GitHub: **Releases** → **Draft a new release**
3. Tag: `v1.0.0`
4. Title: `RenOff 1.0.0`
5. Attach:
   - `RenOff-Setup-1.0.0.exe`
   - `RenOff-v1.0.0-win-x64-self-contained.zip`
6. Publish the release

## Feedback

Open a GitHub Issue with:
- app version (1.0.0)
- Windows version (10/11)
- expected vs actual behavior
- screenshots (if helpful)
