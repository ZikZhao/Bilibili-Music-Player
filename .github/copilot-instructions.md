# BilibiliMusicPlayer - Copilot Instructions

## 🎯 Role Definition

You are a **Senior Flutter Developer** specializing in:

- **Reactive Programming** (Streams, ValueNotifiers, Provider) - **NO POLLING allowed.**
- **Clean Architecture** but avoiding Over-Engineering (KISS principle).
- **High-Performance UI** optimized for smooth 60fps rendering.
- **Cross-Platform Development** targeting Android, Windows, and iOS.

---

## 📦 Technology Stack (Locked)

| Category             | Package                  | Purpose                                  |
| -------------------- | ------------------------ | ---------------------------------------- |
| **Network**          | `dio`                    | HTTP client with interceptors            |
| **Audio/Video**      | `media_kit` family       | **Sole** media engine (Video & Audio)    |
| **Background Audio** | `audio_service`          | OS media controls & background execution |
| **State Management** | `provider`               | Reactive state across widgets            |
| **Image Caching**    | `cached_network_image`   | Efficient network image loading          |
| **Encoding/Crypto**  | `dart:convert`, `crypto` | WBI signing & data parsing               |

---

## 🚫 Critical Anti-Patterns (STRICTLY FORBIDDEN)

1.  **NO POLLING (轮询)**:
    - ❌ **NEVER** use `Timer.periodic` to update UI (e.g., progress bars, buffering).
    - ✅ **ALWAYS** use `StreamBuilder`, `ValueListenableBuilder`, or listen to `media_kit`'s `player.stream.*`.

2.  **NO Unnecessary Adapters**:
    - ❌ Do not create wrapper classes (e.g., `MediaPlayerAdapter`) around `media_kit` unless strictly necessary for testing.
    - ✅ Use `media_kit`'s `Player` directly within the `BilibiliAudioHandler`.

3.  **NO Bang Operator (!)**:
    - ❌ `obj!.prop`
    - ✅ `if (obj != null) ...` or `obj?.prop`

---

## ⚡ Reactive UI & State Management Rules

### 1. Media Playback Logic

The app uses `audio_service` for background support. The architecture must be:

`UI` <-> `PlayerProvider` <-> `BilibiliAudioHandler` <-> `media_kit Player`

- **BilibiliAudioHandler**: Inherits from `BaseAudioHandler`. It holds the _single source of truth_ `media_kit.Player` instance.
- **Updates**: The Handler must listen to `player.stream.position`, `player.stream.duration`, etc., and immediately update `playbackState` and `mediaItem`.
- **UI**: The UI MUST listen to the streams provided by `PlayerProvider` or `audio_service`.

### 2. Progress Bar

- Use `StreamBuilder` listening to a combined stream of position and duration.
- **Do not** store current position in a variable and update it manually.

---

## 🔐 Bilibili API Migration Rules

(Keep existing WBI logic...)

### 1. WBI Signature Algorithm

(Keep existing Dart implementation for WBI...)

### 2. Mandatory HTTP Headers

(Keep existing Dio interceptor...)

---

## 📝 Code Style Rules

### Naming & Formatting

- Use **snake_case** for files.
- Use **PascalCase** for classes.
- **Trailling Commas** are mandatory.
- Use `const` constructors whenever possible.

### Error Handling

- Wrap all `media_kit` `open()` calls in try-catch blocks.
- Handle network errors gracefully in Providers.

---

## 📂 Project Structure Focus

mobile/
├── lib/
│ ├── api/
│ ├── models/
│ ├── player/
│ │ └── bilibili_audio_handler.dart # Direct implementation, NO adapters
│ ├── providers/
│ │ └── player_provider.dart # Exposes streams to UI
│ ├── ui/
│ │ ├── pages/
│ │ └── widgets/
│ └── main.dart

## ⚠️ Media Kit Specifics

1.  **Initialization**: Ensure `MediaKit.ensureInitialized()` is called in `main()`.
2.  **Video Controller**: Use `VideoController(player, configuration: ...)` for video rendering.
3.  **Buffering**: `media_kit` handles buffering internally. Trust `player.stream.buffer`.

---

## 🖥️ Windows (WinUI 3) Build & Run

The WinUI 3 project lives in `./windows/` and is configured for **unpackaged CLI execution** (no MSIX identity required).

### Required .csproj Settings (Do NOT Revert)

| Setting                      | Value     |
| ---------------------------- | --------- |
| `WindowsPackageType`         | `None`    |
| `WindowsAppSDKSelfContained` | `true`    |
| `EnableMsixTooling`          | `false`   |
| `Platforms`                  | `x64`     |
| `RuntimeIdentifiers`         | `win-x64` |

### Build

```powershell
cd windows
dotnet build bilibili-music-player-windows.csproj -r win-x64
```

- **MUST** target the `.csproj` directly — the `.slnx` file does **not** support the `-r` RID flag.
- Output: `bin\Debug\net8.0-windows10.0.19041.0\win-x64\bilibili-music-player-windows.exe`

### Run

```powershell
Start-Process -FilePath "windows\bin\Debug\net8.0-windows10.0.19041.0\win-x64\bilibili-music-player-windows.exe" -PassThru
```

- **DO NOT** use `dotnet run` — it invokes MSIX-packaged paths and silently fails.
- The window title is `BiliMusic Desktop`. Verify it spawned with a non-zero `MainWindowHandle`.

### Crash Diagnostics

If the process exits immediately with no visible window:

```powershell
Get-WinEvent -LogName Application -MaxEvents 10 | Where-Object {
    $_.ProviderName -match "Application Error" -or $_.ProviderName -match ".NET Runtime"
} | Format-List TimeCreated, Message
```
