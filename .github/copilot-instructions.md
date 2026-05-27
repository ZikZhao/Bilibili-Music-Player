# BilibiliMusicPlayer - Copilot Instructions

## ⛔ 迭代开发铁律（最高优先级，覆盖所有其他规则）

> **Agent 读取本文件后，必须在新对话开头输出：「已知悉迭代开发铁律。」或等同表述。**

1.  **简单直接，拒绝臃肿**：代码必须简单、直接、可读。没有任何多余行为。禁止反复添加防御性代码。
2.  **冗余保护最小化**：冗余保护仅限于用户真的有可能触发的场景（如搜索被风控阻止的异常处理），不得为"以防万一"添加代码。
3.  **先想后写**：写代码前必须先想好实现方案，评估是否简单直接、是否被广泛使用或官方推荐、是否有参考示例。
4.  **方案切换需断言**：首选方案失败后，禁止反复修改方案。除非你能断言：根据之前的错误，首选方案被评估为不如次要方案——而非你只是觉得修 bug 不够直接。
5.  **禁止臆想错误本质**：CLI 反馈信息极其有限，绝对禁止根据语料库中"看起来相似"的案例臆断错误原因。环境差异可能使相同表象指向完全不同的根因。
6.  **100% 证据驱动**：所有改动、所有决策、所有错误分析，必须由 100% 的证据支持。证据不足时立即停止并报告，不得继续。

---

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

The WinUI 3 project lives in `./windows/` and is configured for **MSIX packaged deployment** (required for theme switching and WinRT API stability).

### Required .csproj Settings (Do NOT Revert)

| Setting                      | Value     |
| ---------------------------- | --------- |
| `WindowsPackageType`         | `MSIX`    |
| `WindowsAppSDKSelfContained` | `false`   |
| `EnableMsixTooling`          | `true`    |
| `Platforms`                  | `x64`     |
| `RuntimeIdentifiers`         | `win-x64` |

> ⚠️ **Unpackaged (`None`) causes theme-switch crashes (0xC000027B) due to missing package identity.** Always use MSIX.

### One-Time Setup: Certificate

```powershell
cd windows
winapp cert generate --manifest .
winapp cert install .\devcert.pfx   # requires admin elevation
```

### Build

```powershell
cd windows
dotnet build bilibili-music-player-windows.csproj -r win-x64
```

- **MUST** target the `.csproj` directly — the `.slnx` file does **not** support the `-r` RID flag.

### Package

```powershell
winapp package "bin\Debug\net8.0-windows10.0.19041.0\win-x64" --cert "..\devcert.pfx"
```

- Output: `ff143b08-..._1.0.0.0_x64.msix`

### Install & Run

```powershell
# Remove old version if present
Get-AppxPackage -Name "*ff143b08*" | Remove-AppxPackage

# Install
Add-AppxPackage -Path ".\ff143b08-cf1c-4b89-a7b5-3ef19e21fc2e_1.0.0.0_x64.msix"

# Launch via Start Menu (packaged identity)
$pfn = (Get-AppxPackage -Name "*ff143b08*").PackageFamilyName
Start-Process "explorer.exe" -ArgumentList "shell:appsFolder\$pfn!App"
```

- **DO NOT** run the `.exe` directly — packaged apps require `shell:appsFolder` or Start Menu launch.
- **DO NOT** use `dotnet run` — it invokes MSIX-packaged paths and silently fails.

### Crash Diagnostics

If the process exits immediately with no visible window:

```powershell
Get-WinEvent -LogName Application -MaxEvents 10 | Where-Object {
    $_.ProviderName -match "Application Error" -or $_.ProviderName -match ".NET Runtime"
} | Format-List TimeCreated, Message
```
