# 🎯 BiliMusic Desktop — Implementation Plan

> **Last Updated**: 2026-05-26  
> **Source of Truth**: Mobile Flutter app at `mobile/lib/`  
> **Target**: WinUI 3 Desktop app at `windows/`  
> **Goal**: Align Windows implementation to mobile behavior using standard WinUI 3 APIs + CommunityToolkit.Mvvm

---

## 📐 Architecture Principles (from mobile)

| Principle | Mobile (Flutter) | Windows (WinUI 3) |
|-----------|-----------------|-------------------|
| **State Mgmt** | Provider + ChangeNotifier + Streams | CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`) |
| **Reactivity** | `StreamBuilder` / `ValueListenableBuilder` | `{x:Bind}` with `Mode=OneWay` / `Mode=TwoWay` |
| **Audio Engine** | `media_kit` Player | WinUI `MediaPlayerElement` / `MediaPlayer` |
| **Background Play** | `audio_service` | N/A (desktop window stays open) |
| **Persistence** | Hive boxes | `ApplicationData.LocalSettings` + `ApplicationData.LocalFolder` |
| **DI / IoC** | `MultiProvider` | `Microsoft.Extensions.DependencyInjection` |
| **HTTP** | Dio + interceptors | `HttpClient` (existing) |
| **NO Polling** | Streams only | `{x:Bind}` bindings + `INotifyPropertyChanged` |

---

## 🔴 CRITICAL: Before Any Feature Work

These infrastructure items block all other features. Must be done first.

### Phase 0 — Foundation

- [ ] **P0.1** Install `CommunityToolkit.Mvvm` NuGet package  
  `dotnet add package CommunityToolkit.Mvvm`

- [ ] **P0.2** Install `Microsoft.Extensions.DependencyInjection` NuGet package  
  `dotnet add package Microsoft.Extensions.DependencyInjection`

- [ ] **P0.3** Set up DI container in `App.xaml.cs`
  - Register services: `BilibiliClient` (singleton), `HttpClient` (singleton via `IHttpClientFactory`)
  - Register ViewModels as transient/singleton
  - Resolve `MainWindow` from DI, inject its ViewModel
  - **Target file**: `App.xaml.cs`

- [ ] **P0.4** Create `ObservableObject`-based ViewModels for all pages
  - `MainViewModel` → sidebar navigation, now-playing bar
  - `SearchViewModel` → search logic, suggestions, history, results, pagination
  - `LibraryViewModel` → favorites CRUD, sorting, cache status
  - `SettingsViewModel` → theme toggle, fade toggle, cache management

- [ ] **P0.5** Add `Mode=OneWay` to ALL existing `{x:Bind}` bindings
  - Files: `MainWindow.xaml`, `SearchPage.xaml`, `FavoritesPage.xaml`
  - Without this, dynamic data will never update in the UI

- [ ] **P0.6** Fix `HttpRangeStream.Read()` deadlock — remove `.GetAwaiter().GetResult()`  
  - **File**: `Api/HttpRangeStream.cs:90-92`  
  - Replace sync `Read()` with `throw new NotSupportedException("Use ReadAsync")`

---

## 📊 Feature Alignment Matrix

Legend: ✅ Done & Aligned | ⚠️ Partial / Needs Work | ❌ Not Implemented

### 1. 🌐 API Layer

| # | Feature | Mobile | Windows | Status | Notes |
|---|---------|--------|---------|--------|-------|
| 1.1 | WBI Signer | ✅ `WbiSigner` | ✅ `WbiSigner` | ✅ | Identical algorithm, already migrated |
| 1.2 | BilibiliClient init (cookies + WBI keys) | ✅ `_ensureInitialized()` | ✅ `EnsureInitializedAsync()` | ✅ | Same flow: visit bilibili.com → nav API → extract keys |
| 1.3 | Search API (`searchVideos`) | ✅ with pagination + risk detection | ⚠️ no risk detection (412/v_voucher) | ⚠️ | **Missing**: 412 retry, v_voucher risk check |
| 1.4 | Search Suggestions (`fetchSuggestions`) | ✅ `s.search.bilibili.com/main/suggest` | ❌ | ❌ | **Missing entirely** |
| 1.5 | Video Info (`fetchVideoInfo`) | ✅ `/x/web-interface/view` | ✅ `FetchVideoCidAsync()` | ⚠️ | **Missing**: full `VideoDetailInfo` model (owner, stats, desc) |
| 1.6 | Play URL (`fetchPlayUrl`) | ✅ DASH audio + MP4 fallback | ✅ `FetchPlayUrlAsync()` | ⚠️ | **Missing**: DASH audio-only mode (`fnval=16`), MP4 fallback |
| 1.7 | HTTP Range Stream | N/A (Dio handles) | ✅ `HttpRangeStream` | ⚠️ | **Needs fix**: sync-over-async deadlock (see P0.6) |

#### Implementation Tasks — API Layer

- [ ] **A1** Add risk detection to `SearchVideosAsync`  
  Catch 412 → retry; detect `v_voucher` in response → throw with risk message  
  **Target file**: `Api/BilibiliClient.cs`

- [ ] **A2** Add `FetchSuggestionsAsync(string keyword)`  
  Endpoint: `https://s.search.bilibili.com/main/suggest?term=...&...`  
  Returns `List<SuggestionModel>`  
  **Target file**: `Api/BilibiliClient.cs`  
  **New model**: `Models/SuggestionModel.cs`

- [ ] **A3** Add full `VideoDetailInfo` model + parsing  
  Fields: `Bvid`, `Title`, `Desc`, `Cover`, `Aid`, `Cid`, `Pubdate`, `Duration`, `OwnerInfo`, `VideoStat`  
  **New models**: `Models/VideoDetailInfo.cs`, `Models/OwnerInfo.cs`, `Models/VideoStat.cs`

- [ ] **A4** Expand `FetchVideoCidAsync` → `FetchVideoInfoAsync` returning `VideoDetailInfo`

- [ ] **A5** Add DASH audio-only mode to `FetchPlayUrlAsync`  
  When `audioOnly=true`: use `fnval=16`, parse DASH response → extract audio stream with highest bandwidth  
  **New model**: `Models/PlayUrlInfo.cs`

- [ ] **A6** Add DASH→MP4 fallback in `FetchPlayUrlAsync`  
  If no DASH audio found, retry with `fnval=1` (MP4 format)

---

### 2. 🔍 Search

| # | Feature | Mobile | Windows | Status | Notes |
|---|---------|--------|---------|--------|-------|
| 2.1 | Search text input with debounce | ✅ 300ms debounce → suggestions | ❌ no debounce, Enter-only | ❌ | |
| 2.2 | Suggestions dropdown | ✅ ListView overlay | ❌ | ❌ | |
| 2.3 | Search execution + results grid | ✅ GridView with pagination | ⚠️ GridView, no pagination | ⚠️ | |
| 2.4 | Infinite scroll / Load More | ✅ 200px before end | ❌ | ❌ | |
| 2.5 | Search history (persisted) | ✅ Hive → LocalSettings | ⚠️ | ⚠️ | Exists but: no debounce trigger, history not integrated with search flow |
| 2.6 | Clear history | ✅ | ✅ | ✅ | |
| 2.7 | Hot keywords | ❌ (fetched from API) | ⚠️ static seed data | ⚠️ | Mobile uses suggestions API for hot; Windows has hardcoded list |
| 2.8 | Refresh / pull-to-refresh | ✅ RefreshIndicator | ❌ | ❌ | |
| 2.9 | Error state display | ✅ Snackbar / inline error | ✅ ContentDialog | ✅ | |

#### Implementation Tasks — Search

- [ ] **S1** Move search logic from code-behind → `SearchViewModel`  
  - `[ObservableProperty]` for: `SearchText`, `Suggestions`, `Results`, `History`, `HotKeywords`, `IsSearching`, `ErrorMessage`, `HasMore`, `CurrentPage`, `SearchState`  
  - `[RelayCommand]` for: `SearchCommand`, `LoadMoreCommand`, `ClearHistoryCommand`, `RefreshCommand`  
  - **Target files**: `ViewModels/SearchViewModel.cs`, `Pages/SearchPage.xaml` + `.xaml.cs`

- [ ] **S2** Implement 300ms debounce on input → fetch suggestions  
  Use `CancellationTokenSource` pattern (cancel previous on new input)  
  **Target**: `SearchViewModel.OnSearchTextChanged()` partial method

- [ ] **S3** Add suggestions ListView overlay in `SearchPage.xaml`  
  - Show when `SearchState == ShowingSuggestions`  
  - Each item: tappable → sets text + triggers search  
  - Dismiss on focus lost / Enter / Escape

- [ ] **S4** Add pagination (Load More)  
  - Detect scroll near bottom of GridView (`ScrollViewer.ViewChanged` event)  
  - Call `LoadMoreCommand` when within 200px of end  
  - Append results to `ObservableCollection`

- [ ] **S5** Wire `SearchHistory` to auto-save on search + load on startup  
  Already partially implemented in `MainViewModel` — move to `SearchViewModel` and persist via `ApplicationData.LocalSettings`

- [ ] **S6** Add `x:Bind, Mode=OneWay` to all search UI bindings

- [ ] **S7** Add error state UI (TextBlock with error icon when `ErrorMessage != null`)

---

### 3. 🎵 Audio Playback (Core Feature)

| # | Feature | Mobile | Windows | Status | Notes |
|---|---------|--------|---------|--------|-------|
| 3.1 | Audio-only playback engine | ✅ `media_kit` Player | ❌ | ❌ | WinUI `MediaPlayer` can play audio |
| 3.2 | Play/Pause | ✅ | ❌ (mock UI only) | ❌ | |
| 3.3 | Seek (progress bar drag) | ✅ | ❌ (mock UI only) | ❌ | |
| 3.4 | Position + Duration display | ✅ streams | ❌ (static labels) | ❌ | |
| 3.5 | Buffering indicator | ✅ `player.stream.buffer` | ❌ | ❌ | |
| 3.6 | Previous / Next track | ✅ | ❌ (mock buttons) | ❌ | |
| 3.7 | Now-playing bar (bottom) | ✅ MiniPlayer | ⚠️ static mock | ⚠️ | WinUI has static mock in MainWindow; needs binding |
| 3.8 | Full audio player page | ✅ `AudioPlayerPage` | ❌ | ❌ | |
| 3.9 | Volume fade-in/out | ✅ 800ms parabolic | ❌ | ❌ | |
| 3.10 | Play modes (Loop/Single/Shuffle) | ✅ enum + logic | ❌ | ❌ | |

#### Implementation Tasks — Audio Playback

- [ ] **P1** Create `AudioPlayerService` wrapping WinUI `MediaPlayer`  
  - Single `MediaPlayer` instance (no adapters, per mobile rule)  
  - Methods: `PlayAsync(url, headers)`, `Pause()`, `Seek(TimeSpan)`, `Stop()`  
  - Properties: `Position`, `Duration`, `IsPlaying`, `BufferingProgress`, `Volume`  
  - Events → `INotifyPropertyChanged` for real-time binding  
  - **Target file**: `Services/AudioPlayerService.cs`  
  - **Register** as singleton in DI

- [ ] **P2** Create `PlayerViewModel`  
  - `[ObservableProperty]` for: `CurrentTrack`, `IsPlaying`, `Position`, `Duration`, `Progress`, `BufferingProgress`, `Volume`, `CurrentIndex`, `QueueCount`, `PlayMode`, `IsFading`  
  - `[RelayCommand]` for: `PlayCommand`, `PauseCommand`, `TogglePlayCommand`, `SeekCommand`, `NextCommand`, `PreviousCommand`, `CyclePlayModeCommand`  
  - Listen to `MediaPlayer` events → update observable properties  
  - **Target file**: `ViewModels/PlayerViewModel.cs`

- [ ] **P3** Implement `PlayerViewModel.PlayVideoAsync(VideoModel)`  
  Flow matching mobile exactly:  
  1. Set `_pendingBvid` race-condition lock  
  2. Check cache → if cached, use local file  
  3. If not cached: `FetchVideoInfoAsync` → `FetchPlayUrlAsync(audioOnly: true)`  
  4. Trigger background download via `CacheService`  
  5. Cancel if `_pendingBvid` changed  
  6. `_mediaPlayer.Source = MediaSource.CreateFromUri(uri)` with headers  
  7. `_mediaPlayer.Play()`

- [ ] **P4** Wire Play/Pause/Next/Previous in `MainWindow.xaml` now-playing bar  
  Replace mock buttons with `{x:Bind PlayerViewModel.TogglePlayCommand}` etc.  
  Bind `Position`, `Duration`, `Progress` to progress bar  
  Bind `CurrentTrack.Title`, `CurrentTrack.Artist` to labels

- [ ] **P5** Implement volume fade-in/out  
  - Fade-in: 0→100 over 800ms, linear  
  - Fade-out: 100→0 over 800ms, parabolic ($t^2$)  
  - Use `DispatcherTimer` at 50ms intervals (16 steps)  
  - During fade: set `IsFading = true` to prevent UI flicker

- [ ] **P6** Create `AudioPlayerPage` (full-screen player)  
  Layout matching mobile:  
  - Cover image (from VideoModel)  
  - Track title (marquee if long) + artist  
  - Progress slider (`Slider` bound to `Position/Duration`)  
  - Time labels (`01:24 / 04:13`)  
  - Control buttons: PlayMode | Previous | Play/Pause | Next | Playlist  
  - Volume slider  
  - **Target file**: `Pages/AudioPlayerPage.xaml` + `.xaml.cs`

- [ ] **P7** Implement Playlist management  
  - `ObservableCollection<VideoModel> Playlist` in `PlayerViewModel`  
  - `AddToPlaylistCommand`, `RemoveFromPlaylistCommand`, `ClearPlaylistCommand`  
  - `SetPlaylistCommand(List<VideoModel>, startIndex)`  
  - Playlist sheet UI in `AudioPlayerPage`

- [ ] **P8** Implement PlayMode logic (Loop / Single / Shuffle)  
  - `PlayMode` enum: `Loop`, `Single`, `Shuffle`  
  - `CyclePlayModeCommand`: Loop→Single→Shuffle→Loop  
  - `OnMediaEnded`:  
    - Loop: next track or wrap to first  
    - Single: replay same track  
    - Shuffle: random track (not same as current)

---

### 4. 📚 Library / Favorites

| # | Feature | Mobile | Windows | Status | Notes |
|---|---------|--------|---------|--------|-------|
| 4.1 | Favorites list display | ✅ ListView | ⚠️ static seed data | ⚠️ | UI exists, needs real data |
| 4.2 | Add/Remove favorites | ✅ toggle + Hive | ❌ | ❌ | |
| 4.3 | Favorite state indicator | ✅ heart icon | ❌ | ❌ | |
| 4.4 | Sort (date, title) | ✅ PopupMenu | ⚠️ static sort label | ⚠️ | |
| 4.5 | Auto-download on favorite | ✅ background | ❌ | ❌ | |
| 4.6 | Play from favorites | ✅ tap → play from index | ❌ | ❌ | |

#### Implementation Tasks — Library

- [ ] **L1** Create `LibraryViewModel`  
  - `[ObservableProperty]` for: `Favorites` (ObservableCollection), `SortOption`, `SortOptions`, `IsEmpty`  
  - `[RelayCommand]` for: `ToggleFavoriteCommand(VideoModel)`, `SortCommand(SortOption)`, `PlayFromFavoriteCommand(VideoModel)`, `RemoveFavoriteCommand(VideoModel)`  
  - **Target file**: `ViewModels/LibraryViewModel.cs`

- [ ] **L2** Implement favorites persistence  
  Use `ApplicationData.LocalFolder` + JSON file (`favorites.json`)  
  Load on ViewModel init, save on every add/remove  
  Match mobile's `VideoModel` Hive schema

- [ ] **L3** Wire `FavoritesPage.xaml` to `LibraryViewModel`  
  Replace `{x:Bind ViewModel.Favorites}` with real binding  
  Add tap handler → `PlayFromFavoriteCommand`  
  Add sort dropdown → `SortCommand`  
  Add swipe-to-delete or context menu → `RemoveFavoriteCommand`

- [ ] **L4** Add auto-download trigger on favorite  
  When `ToggleFavoriteCommand` adds a video → check cache → if not cached, start background download

---

### 5. 📦 Caching

| # | Feature | Mobile | Windows | Status | Notes |
|---|---------|--------|---------|--------|-------|
| 5.1 | Audio file caching (.m4s) | ✅ | ❌ | ❌ | |
| 5.2 | Download progress tracking | ✅ `DownloadProgress` stream | ❌ | ❌ | |
| 5.3 | Background download | ✅ `downloadInBackground()` | ❌ | ❌ | |
| 5.4 | Atomic file rename (.tmp → .m4s) | ✅ | ❌ | ❌ | |
| 5.5 | Cache size calculation | ✅ formatted string | ❌ | ❌ | |
| 5.6 | Clear cache | ✅ | ⚠️ static UI button | ⚠️ | UI mock exists in SettingsPage |

#### Implementation Tasks — Caching

- [ ] **C1** Create `CacheService`  
  - Cache directory: `ApplicationData.LocalFolder\audio_cache\`  
  - `GetAudioPathAsync(bvid)` → `string?` (file path if cached)  
  - `IsCachedAsync(bvid)` → `bool`  
  - `DownloadAudioAsync(url, bvid, progress)` → downloads to `.tmp`, atomic rename to `{bvid}.m4s`  
  - `DownloadInBackground(url, bvid)` → fire-and-forget with progress events  
  - `GetFormattedCacheSizeAsync()` → `string` (e.g., "42.9 MB")  
  - `ClearCacheAsync()`  
  - Progress: `IObservable<DownloadProgress>` or event-based  
  - **Target file**: `Services/CacheService.cs`  
  - **Register** as singleton in DI

- [ ] **C2** Create `DownloadProgress` model  
  Properties: `Bvid`, `ReceivedBytes`, `TotalBytes`, `Progress` (0.0-1.0), `IsComplete`, `Error`  
  **Target file**: `Models/DownloadProgress.cs`

- [ ] **C3** Wire `CacheService` to `SettingsViewModel`  
  - `ClearCacheCommand` → calls `CacheService.ClearCacheAsync()`  
  - `CacheSize` property → calls `CacheService.GetFormattedCacheSizeAsync()` on load

---

### 6. ⚙️ Settings

| # | Feature | Mobile | Windows | Status | Notes |
|---|---------|--------|---------|--------|-------|
| 6.1 | Theme mode (Auto/Light/Dark) | ✅ segmented button + Hive | ⚠️ static mock | ⚠️ | UI exists, no logic |
| 6.2 | Fade playback toggle | ✅ Switch + Hive | ⚠️ static ToggleSwitch | ⚠️ | UI exists, no logic |
| 6.3 | Auto-play toggle | ✅ | ⚠️ static ToggleSwitch | ⚠️ | UI exists, no logic |
| 6.4 | Audio quality selector | ✅ (mock) | ⚠️ static label | ⚠️ | |
| 6.5 | Clear cache | ✅ with size display | ⚠️ static label "42.9 MB" | ⚠️ | |

#### Implementation Tasks — Settings

- [ ] **ST1** Create `SettingsViewModel`  
  - `[ObservableProperty]` for: `ThemeMode` (System/Light/Dark), `EnableFade`, `EnableAutoPlay`, `AudioQuality`, `CacheSize`  
  - `[RelayCommand]` for: `ClearCacheCommand`  
  - Persist settings to `ApplicationData.LocalSettings`  
  - Load settings on initialization  
  - **Target file**: `ViewModels/SettingsViewModel.cs`

- [ ] **ST2** Wire `SettingsPage.xaml` to `SettingsViewModel`  
  - Theme segmented control → bound to `ThemeMode`  
  - Fade ToggleSwitch → bound to `EnableFade`  
  - Auto-play ToggleSwitch → bound to `EnableAutoPlay`  
  - Cache size → bound to `CacheSize`  
  - Clear cache button → bound to `ClearCacheCommand`

- [ ] **ST3** Implement theme switching  
  When `ThemeMode` changes:  
  - `System`: follow OS theme  
  - `Light`: force Light  
  - `Dark`: force Dark  
  - Apply via `App.Current.RequestedTheme` or `FrameworkElement.RequestedTheme` on MainWindow

---

### 7. 🎨 UI / Theming

| # | Feature | Mobile | Windows | Status | Notes |
|---|---------|--------|---------|--------|-------|
| 7.1 | Light/Dark/System theme | ✅ Material 3 | ❌ light-only | ❌ | |
| 7.2 | Brand colors (Bilibili pink) | ✅ `#FB7299` | ⚠️ hardcoded Rose | ⚠️ | Need theme-aware brushes |
| 7.3 | Typography styles | ✅ Material 3 text styles | ❌ raw FontSize | ❌ | |
| 7.4 | Mica/Backdrop effect | N/A | ❌ | ❌ | |
| 7.5 | TitleBar customization | N/A | ❌ | ❌ | |
| 7.6 | NavigationView sidebar | N/A (bottom nav) | ❌ custom sidebar | ❌ | Should use WinUI NavigationView |

#### Implementation Tasks — UI

- [ ] **U1** Replace custom sidebar with `NavigationView`  
  - `NavigationView.PaneDisplayMode="LeftCompact"`  
  - Items: Search, Library, Settings  
  - `NavigationView.SelectionChanged` → navigate Frame  
  - Remove manual `ListView` sidebar from `MainWindow.xaml`

- [ ] **U2** Add theme-aware resource dictionaries  
  - `Themes/LightColors.xaml` — light theme brushes  
  - `Themes/DarkColors.xaml` — dark theme brushes  
  - Bilibili pink as `{ThemeResource BilibiliPinkBrush}`  
  - Migrate all `{StaticResource Slate...}` → `{ThemeResource TextFillColor...}` or custom theme brushes

- [ ] **U3** Replace raw `FontSize`/`FontWeight` with WinUI typography styles  
  - `TitleTextBlockStyle`, `SubtitleTextBlockStyle`, `BodyTextBlockStyle`, `CaptionTextBlockStyle`

- [ ] **U4** Add `MicaBackdrop` / `DesktopAcrylicBackdrop` to `MainWindow`  
  - `SystemBackdrop = new MicaBackdrop()`

- [ ] **U5** Customize title bar  
  - `ExtendsContentIntoTitleBar = true`  
  - `SetTitleBar()` with custom drag region

- [ ] **U6** Add `AutomationProperties.AutomationId` to ALL interactive controls  
  Every Button, TextBox, ListView, ToggleSwitch, NavigationViewItem: unique AutomationId

---

### 8. 🎬 Video Preview / Detail

| # | Feature | Mobile | Windows | Status | Notes |
|---|---------|--------|---------|--------|-------|
| 8.1 | Video preview dialog | ✅ media_kit video player | ✅ ContentDialog + MediaPlayerElement | ✅ | |
| 8.2 | Video detail page | ✅ full page with stats | ❌ | ❌ | |
| 8.3 | Wakelock (screen stay on) | ✅ `wakelock_plus` | N/A | N/A | Desktop doesn't need |
| 8.4 | Pause audio on video entry | ✅ | ❌ | ❌ | |

#### Implementation Tasks — Video

- [ ] **V1** Move video preview logic from code-behind → `SearchViewModel` (or dedicated `VideoPreviewViewModel`)  
  - `PreviewVideoCommand`  
  - `PreviewUri`, `PreviewTitle`, `PreviewMeta` as observable properties  
  - Stream management in ViewModel

- [ ] **V2** When video preview opens, pause audio playback (if playing)  
  Call `PlayerViewModel.PauseCommand`

- [ ] **V3** Add `VideoDetailPage` (future stretch goal)  
  Full page with video info: title, author avatar, description, stats, favorite button

---

### 9. 🧹 Code Quality (from Code Review)

- [ ] **Q1** Replace all `async void` event handlers with `[RelayCommand]` bindings  
  SearchPage: `OnSearchKeyDown`, `OnKeywordClick`, `OnClearHistoryClick`, `OnVideoClick`  
  Use `Command` + `CommandParameter` in XAML instead

- [ ] **Q2** Fix `HttpRangeStream.Read()` — remove `GetAwaiter().GetResult()`

- [ ] **Q3** Add `NavigationCacheMode="Disabled"` to SearchPage (heavy resources)

- [ ] **Q4** Remove hardcoded seed data from `MainViewModel.Seed()` — load from persistence

- [ ] **Q5** Consolidate duplicate HTTP header setup  
  `BilibiliClient.cs` and `SearchPage.xaml.cs` both set User-Agent/Referer headers  
  Move to a shared `HttpClientFactory` or `BilibiliClient` static constructor

- [ ] **Q6** Add `using` statements to all disposable references  
  Verify `JsonDocument`, `HttpResponseMessage`, `Stream` all properly disposed

- [ ] **Q7** Remove `PreviewHttpClient` static field — use singleton from DI

---

## 📋 Dependency Graph (Implementation Order)

```
Phase 0  P0.1 → P0.2 → P0.3 → P0.4 → P0.5 → P0.6
           ↓
Phase 1  Q1..Q7 (code quality — fix before adding features)
           ↓
Phase 2  A1..A6 (API gaps)
           ↓
Phase 3  C1..C3 (caching infrastructure) ──┐
           ↓                                │
Phase 4  P1..P3 (audio player service + VM)  │
           ↓                                │
Phase 5  S1..S7 (search VM + UI) ← depends on A2,A3
           ↓
Phase 6  L1..L4 (library VM + persistence) ← depends on C,P
           ↓
Phase 7  P4..P8 (playback UI: now-playing bar, full player, play modes) ← depends on P1..P3
           ↓
Phase 8  V1..V3 (video preview refactor) ← depends on P
           ↓
Phase 9  ST1..ST3 (settings VM + theme switching) ← depends on C
           ↓
Phase 10 U1..U6 (UI polish: NavigationView, themes, accessibility) ← depends on ST
```

---

## 📁 Target File Structure (after all phases)

```
windows/
├── IMPLEMENTATION_PLAN.md        ← this file
├── bilibili-music-player-windows.csproj
├── App.xaml                       ← DI container init
├── App.xaml.cs
├── MainWindow.xaml                ← NavigationView + now-playing bar
├── MainWindow.xaml.cs
├── Api/
│   ├── BilibiliClient.cs          ← +suggestions, +risk detection, +DASH/mp4
│   ├── HttpRangeStream.cs         ← fix deadlock
│   └── WbiSigner.cs              ← same
├── Models/
│   ├── BilibiliApiException.cs
│   ├── DownloadProgress.cs        ← NEW
│   ├── OwnerInfo.cs               ← NEW
│   ├── PlayUrlInfo.cs             ← NEW
│   ├── SearchResult.cs
│   ├── SuggestionModel.cs         ← NEW
│   ├── TrackItem.cs               ← merge into VideoModel
│   ├── VideoDetailInfo.cs         ← NEW
│   ├── VideoModel.cs              ← RENAMED from VideoPreviewItem, add aid/mid/cover/playCount/etc.
│   └── VideoStat.cs               ← NEW
├── Services/
│   ├── AudioPlayerService.cs      ← NEW (wraps MediaPlayer)
│   └── CacheService.cs            ← NEW
├── ViewModels/
│   ├── LibraryViewModel.cs        ← NEW
│   ├── MainViewModel.cs           ← refactored: ObservableObject, DI, no seed data
│   ├── PlayerViewModel.cs         ← NEW
│   ├── SearchViewModel.cs         ← NEW
│   └── SettingsViewModel.cs       ← NEW
├── Pages/
│   ├── AudioPlayerPage.xaml       ← NEW
│   ├── AudioPlayerPage.xaml.cs
│   ├── FavoritesPage.xaml         ← renamed? or keep as LibraryPage
│   ├── FavoritesPage.xaml.cs
│   ├── SearchPage.xaml            ← refactored: use SearchViewModel
│   ├── SearchPage.xaml.cs
│   ├── SettingsPage.xaml          ← refactored: use SettingsViewModel
│   ├── SettingsPage.xaml.cs
│   └── VideoDetailPage.xaml       ← NEW (stretch)
├── Themes/
│   ├── LightColors.xaml           ← NEW
│   └── DarkColors.xaml            ← NEW
└── Strings/
    └── zh-CN/
        └── Resources.resw         ← NEW (localization)
```

---

## 🚀 Getting Started for a New Agent

1. Read this entire plan first
2. Start with **Phase 0** (infrastructure) — it blocks everything
3. Follow the dependency graph strictly
4. Each checkbox = one atomic commit
5. After each phase, run `dotnet build` to verify no regressions
6. Consult `mobile/lib/` for mobile reference implementation when details are unclear
7. Follow WinUI code review rules from the `winui-code-review` skill:
   - `AutomationProperties.AutomationId` on every control
   - `{x:Bind}` with explicit `Mode`
   - `ObservableObject` + `[ObservableProperty]` + `[RelayCommand]`
   - No hardcoded colors, use `{ThemeResource}`
   - No `GetAwaiter().GetResult()`
