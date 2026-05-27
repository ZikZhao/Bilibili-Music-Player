# 🎯 BiliMusic Desktop — Implementation Plan

> **Last Updated**: 2026-05-26  
> **Source of Truth**: Mobile Flutter app at `mobile/lib/`  
> **Target**: WinUI 3 Desktop app at `windows/`  
> **Goal**: Align Windows implementation to mobile behavior using standard WinUI 3 APIs + CommunityToolkit.Mvvm

---

## 📐 Architecture Principles (from mobile)

| Principle           | Mobile (Flutter)                           | Windows (WinUI 3)                                                                    |
| ------------------- | ------------------------------------------ | ------------------------------------------------------------------------------------ |
| **State Mgmt**      | Provider + ChangeNotifier + Streams        | CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`) |
| **Reactivity**      | `StreamBuilder` / `ValueListenableBuilder` | `{x:Bind}` with `Mode=OneWay` / `Mode=TwoWay`                                        |
| **Audio Engine**    | `media_kit` Player                         | WinUI `MediaPlayerElement` / `MediaPlayer`                                           |
| **Background Play** | `audio_service`                            | N/A (desktop window stays open)                                                      |
| **Persistence**     | Hive boxes                                 | `ApplicationData.LocalSettings` + `ApplicationData.LocalFolder`                      |
| **DI / IoC**        | `MultiProvider`                            | `Microsoft.Extensions.DependencyInjection`                                           |
| **HTTP**            | Dio + interceptors                         | `HttpClient` (existing)                                                              |
| **NO Polling**      | Streams only                               | `{x:Bind}` bindings + `INotifyPropertyChanged`                                       |

---

## 🔴 CRITICAL: Before Any Feature Work

These infrastructure items block all other features. Must be done first.

### Phase 0 — Foundation

- [x] **P0.1** Install `CommunityToolkit.Mvvm` NuGet package  
       `dotnet add package CommunityToolkit.Mvvm`

- [x] **P0.2** Install `Microsoft.Extensions.DependencyInjection` NuGet package  
       `dotnet add package Microsoft.Extensions.DependencyInjection`

- [x] **P0.3** Set up DI container in `App.xaml.cs`
  - Register services: `BilibiliClient` (singleton), `HttpClient` (singleton via `IHttpClientFactory`)
  - Register ViewModels as transient/singleton
  - Resolve `MainWindow` from DI, inject its ViewModel
  - **Target file**: `App.xaml.cs`

- [x] **P0.4** Create `ObservableObject`-based ViewModels for all pages
  - `MainViewModel` → sidebar navigation, now-playing bar
  - `SearchViewModel` → search logic, suggestions, history, results, pagination
  - `LibraryViewModel` → favorites CRUD, sorting, cache status
  - `SettingsViewModel` → theme toggle, fade toggle, cache management

- [x] **P0.5** Add `Mode=OneWay` to ALL existing `{x:Bind}` bindings
  - Files: `MainWindow.xaml`, `SearchPage.xaml`, `FavoritesPage.xaml`
  - Without this, dynamic data will never update in the UI

- [x] **P0.6** Fix `HttpRangeStream.Read()` deadlock — remove `.GetAwaiter().GetResult()`
  - **File**: `Api/HttpRangeStream.cs:90-92`
  - Replace sync `Read()` with `throw new NotSupportedException("Use ReadAsync")`

---

## 📊 Feature Alignment Matrix

Legend: ✅ Done & Aligned | ⚠️ Partial / Needs Work | ❌ Not Implemented

### 1. 🌐 API Layer

| #   | Feature                                  | Mobile                                  | Windows                           | Status | Notes                                                       |
| --- | ---------------------------------------- | --------------------------------------- | --------------------------------- | ------ | ----------------------------------------------------------- |
| 1.1 | WBI Signer                               | ✅ `WbiSigner`                          | ✅ `WbiSigner`                    | ✅     | Identical algorithm, already migrated                       |
| 1.2 | BilibiliClient init (cookies + WBI keys) | ✅ `_ensureInitialized()`               | ✅ `EnsureInitializedAsync()`     | ✅     | Same flow: visit bilibili.com → nav API → extract keys      |
| 1.3 | Search API (`searchVideos`)              | ✅ with pagination + risk detection     | ✅ risk detection (412/v_voucher) | ✅     | A1 done: 412 retry + v_voucher check                        |
| 1.4 | Search Suggestions (`fetchSuggestions`)  | ✅ `s.search.bilibili.com/main/suggest` | ✅ `FetchSuggestionsAsync()`      | ✅     | A2 done: new endpoint + SuggestionModel                     |
| 1.5 | Video Info (`fetchVideoInfo`)            | ✅ `/x/web-interface/view`              | ✅ `FetchVideoInfoAsync()`        | ✅     | A3/A4 done: full VideoDetailInfo model (owner, stats, desc) |
| 1.6 | Play URL (`fetchPlayUrl`)                | ✅ DASH audio + MP4 fallback            | ✅ DASH audio + MP4 fallback      | ✅     | A5/A6 done: fnval=16 DASH + MP4 fallback                    |
| 1.7 | HTTP Range Stream                        | N/A (Dio handles)                       | ✅ `HttpRangeStream`              | ⚠️     | **Needs fix**: sync-over-async deadlock (see P0.6)          |

#### Implementation Tasks — API Layer

- [x] **A1** Add risk detection to `SearchVideosAsync`  
       Catch 412 → retry; detect `v_voucher` in response → throw with risk message  
       **Target file**: `Api/BilibiliClient.cs`

- [x] **A2** Add `FetchSuggestionsAsync(string keyword)`  
       Endpoint: `https://s.search.bilibili.com/main/suggest?term=...&...`  
       Returns `List<SuggestionModel>`  
       **Target file**: `Api/BilibiliClient.cs`  
       **New model**: `Models/SuggestionModel.cs`

- [x] **A3** Add full `VideoDetailInfo` model + parsing  
       Fields: `Bvid`, `Title`, `Desc`, `Cover`, `Aid`, `Cid`, `Pubdate`, `Duration`, `OwnerInfo`, `VideoStat`  
       **New models**: `Models/VideoDetailInfo.cs`, `Models/OwnerInfo.cs`, `Models/VideoStat.cs`

- [x] **A4** Expand `FetchVideoCidAsync` → `FetchVideoInfoAsync` returning `VideoDetailInfo`

- [x] **A5** Add DASH audio-only mode to `FetchPlayUrlAsync`  
       When `audioOnly=true`: use `fnval=16`, parse DASH response → extract audio stream with highest bandwidth  
       **New model**: `Models/PlayUrlInfo.cs`

- [x] **A6** Add DASH→MP4 fallback in `FetchPlayUrlAsync`  
       If no DASH audio found, retry with `fnval=1` (MP4 format)

---

### 2. 🔍 Search

| #   | Feature                         | Mobile                          | Windows                    | Status | Notes                                                                    |
| --- | ------------------------------- | ------------------------------- | -------------------------- | ------ | ------------------------------------------------------------------------ |
| 2.1 | Search text input with debounce | ✅ 300ms debounce → suggestions | ❌ no debounce, Enter-only | ❌     |                                                                          |
| 2.2 | Suggestions dropdown            | ✅ ListView overlay             | ❌                         | ❌     |                                                                          |
| 2.3 | Search execution + results grid | ✅ GridView with pagination     | ⚠️ GridView, no pagination | ⚠️     |                                                                          |
| 2.4 | Infinite scroll / Load More     | ✅ 200px before end             | ❌                         | ❌     |                                                                          |
| 2.5 | Search history (persisted)      | ✅ Hive → LocalSettings         | ⚠️                         | ⚠️     | Exists but: no debounce trigger, history not integrated with search flow |
| 2.6 | Clear history                   | ✅                              | ✅                         | ✅     |                                                                          |
| 2.7 | Hot keywords                    | ❌ (fetched from API)           | ⚠️ static seed data        | ⚠️     | Mobile uses suggestions API for hot; Windows has hardcoded list          |
| 2.8 | Refresh / pull-to-refresh       | ✅ RefreshIndicator             | ❌                         | ❌     |                                                                          |
| 2.9 | Error state display             | ✅ Snackbar / inline error      | ✅ ContentDialog           | ✅     |                                                                          |

#### Implementation Tasks — Search

- [x] **S1** Move search logic from code-behind → `SearchViewModel`
  - `[ObservableProperty]` for: `SearchText`, `Suggestions`, `Results`, `History`, `HotKeywords`, `IsSearching`, `ErrorMessage`, `HasMore`, `CurrentPage`, `SearchState`
  - `[RelayCommand]` for: `SearchCommand`, `LoadMoreCommand`, `ClearHistoryCommand`, `RefreshCommand`
  - **Target files**: `ViewModels/SearchViewModel.cs`, `Pages/SearchPage.xaml` + `.xaml.cs`

- [x] **S2** Implement 300ms debounce on input → fetch suggestions
      Use `CancellationTokenSource` pattern (cancel previous on new input)
      **Target**: `SearchViewModel.OnSearchTextChanged()` partial method

- [x] **S3** Add suggestions ListView overlay in `SearchPage.xaml`
  - Show when `SearchState == ShowingSuggestions`
  - Each item: tappable → sets text + triggers search
  - Dismiss on focus lost / Enter / Escape

- [x] **S4** Add pagination (Load More)
  - Detect scroll near bottom of GridView (`ScrollViewer.ViewChanged` event)
  - Call `LoadMoreCommand` when within 200px of end
  - Append results to `ObservableCollection`

- [x] **S5** Wire `SearchHistory` to auto-save on search + load on startup
      Already partially implemented in `MainViewModel` — move to `SearchViewModel` and persist via `ApplicationData.LocalSettings`

- [x] **S6** Add `x:Bind, Mode=OneWay` to all search UI bindings

- [x] **S7** Add error state UI (TextBlock with error icon when `ErrorMessage != null`)

---

### 3. 🎵 Audio Playback (Core Feature)

| #    | Feature                          | Mobile                    | Windows            | Status | Notes                                              |
| ---- | -------------------------------- | ------------------------- | ------------------ | ------ | -------------------------------------------------- |
| 3.1  | Audio-only playback engine       | ✅ `media_kit` Player     | ❌                 | ❌     | WinUI `MediaPlayer` can play audio                 |
| 3.2  | Play/Pause                       | ✅                        | ❌ (mock UI only)  | ❌     |                                                    |
| 3.3  | Seek (progress bar drag)         | ✅                        | ❌ (mock UI only)  | ❌     |                                                    |
| 3.4  | Position + Duration display      | ✅ streams                | ❌ (static labels) | ❌     |                                                    |
| 3.5  | Buffering indicator              | ✅ `player.stream.buffer` | ❌                 | ❌     |                                                    |
| 3.6  | Previous / Next track            | ✅                        | ❌ (mock buttons)  | ❌     |                                                    |
| 3.7  | Now-playing bar (bottom)         | ✅ MiniPlayer             | ⚠️ static mock     | ⚠️     | WinUI has static mock in MainWindow; needs binding |
| 3.8  | Full audio player page           | ✅ `AudioPlayerPage`      | ❌                 | ❌     |                                                    |
| 3.9  | Volume fade-in/out               | ✅ 800ms parabolic        | ❌                 | ❌     |                                                    |
| 3.10 | Play modes (Loop/Single/Shuffle) | ✅ enum + logic           | ❌                 | ❌     |                                                    |

#### Implementation Tasks — Audio Playback

- [x] **P1** Create `AudioPlayerService` wrapping WinUI `MediaPlayer`
  - Single `MediaPlayer` instance (no adapters, per mobile rule)
  - Methods: `PlayAsync(uri, headers)`, `Pause()`, `Seek(TimeSpan)`, `Stop()`
  - Properties: `Position`, `Duration`, `IsPlaying`, `BufferingProgress`, `Volume`
  - Events → `INotifyPropertyChanged` for real-time binding (250ms `DispatcherTimer` polling)
  - **Target file**: `Services/AudioPlayerService.cs`
  - **Register** as singleton in DI

- [x] **P2** Create `PlayerViewModel`
  - `[ObservableProperty]` for: `CurrentTrack`, `IsPlaying`, `Position`, `Duration`, `Progress`, `BufferingProgress`, `Volume`, `CurrentIndex`, `QueueCount`, `PlayMode`, `IsFading`
  - `[RelayCommand]` for: `Play`, `Pause`, `TogglePlay`, `Seek`, `Next`, `Previous`, `CyclePlayMode`
  - Listen to `AudioPlayerService` events → update observable properties via `DispatcherQueue`
  - Playlist management: `SetPlaylistAsync()`, `AddToPlaylist()`, `RemoveFromPlaylist()`, `ClearPlaylist()`
  - PlayMode logic: Loop / Single / Shuffle
  - **Target file**: `ViewModels/PlayerViewModel.cs`

- [x] **P3** Implement `PlayerViewModel.PlayVideoAsync(VideoModel)`  
       Flow matching mobile exactly:
  1. Set `_pendingBvid` race-condition lock
  2. Check cache → if cached, use local file path
  3. If not cached: `FetchVideoInfoAsync` → `FetchPlayUrlAsync(audioOnly: true)`
  4. Trigger background download via `CacheService` (fire-and-forget)
  5. Cancel if `_pendingBvid` changed (multiple checkpoints)
  6. `_audioPlayer.PlayAsync(uri)` — `MediaSource.CreateFromUri`
  7. Set `IsPlaying = true`

- [x] **P4** Wire Play/Pause/Next/Previous in `MainWindow.xaml` now-playing bar  
       ✅ Replace mock buttons with `{x:Bind PlayerViewModel.TogglePlayCommand}` etc.  
       ✅ Bind `Position`, `Duration`, `Progress` to progress bar  
       ✅ Bind `CurrentTrack.Title`, `CurrentTrack.Artist` to labels

- [x] **P5** Implement volume fade-in/out
  - ✅ Fade-in: 0→100 over 800ms, linear
  - ✅ Fade-out: 100→0 over 800ms, parabolic ($t^2$)
  - ✅ Use `DispatcherTimer` at 50ms intervals (16 steps)
  - ✅ During fade: set `IsFading = true` to prevent UI flicker

- [x] **P6** Create `AudioPlayerPage` (full-screen player)  
       ✅ Layout matching mobile:
  - Cover image (from VideoModel)
  - Track title (marquee if long) + artist
  - Progress slider (`Slider` bound to `Position/Duration`)
  - Time labels (`01:24 / 04:13`)
  - Control buttons: PlayMode | Previous | Play/Pause | Next | Playlist
  - Volume slider
  - **Target file**: `Pages/AudioPlayerPage.xaml` + `.xaml.cs`

- [x] **P7** Implement Playlist management
  - ✅ `ObservableCollection<VideoModel> Playlist` in `PlayerViewModel`
  - ✅ `AddToPlaylistCommand`, `RemoveFromPlaylistCommand`, `ClearPlaylistCommand`
  - ✅ `SetPlaylistCommand(List<VideoModel>, startIndex)`
  - ✅ Playlist sheet UI in `AudioPlayerPage`

- [x] **P8** Implement PlayMode logic (Loop / Single / Shuffle)
  - ✅ `PlayMode` enum: `Loop`, `Single`, `Shuffle`
  - ✅ `CyclePlayModeCommand`: Loop→Single→Shuffle→Loop
  - ✅ `OnMediaEnded`:
    - Loop: next track or wrap to first
    - Single: replay same track
    - Shuffle: random track (not same as current)

---

### 4. 📚 Library / Favorites

| #   | Feature                   | Mobile                   | Windows                       | Status | Notes                               |
| --- | ------------------------- | ------------------------ | ----------------------------- | ------ | ----------------------------------- |
| 4.1 | Favorites list display    | ✅ ListView              | ✅ real ListView + VideoModel | ✅     | DataTemplate with cover, title, UP  |
| 4.2 | Add/Remove favorites      | ✅ toggle + Hive         | ✅ ToggleFavorite via VM      | ✅     | Remove button per row               |
| 4.3 | Favorite state indicator  | ✅ heart icon            | ✅ view count indicator       | ✅     | Plays indicator + cover image       |
| 4.4 | Sort (date, title)        | ✅ PopupMenu             | ✅ ComboBox bound to VM       | ✅     | 收藏时间 / 标题                     |
| 4.5 | Auto-download on favorite | ✅ background            | ✅ CacheService background    | ✅     | Triggered in ToggleFavoriteAsync    |
| 4.6 | Play from favorites       | ✅ tap → play from index | ✅ tap → SetPlaylistAsync     | ✅     | ItemClick → PlayFromFavoriteCommand |

#### Implementation Tasks — Library

- [x] **L1** Create `LibraryViewModel`
  - `[ObservableProperty]` for: `Favorites` (ObservableCollection`<VideoModel>`), `SortOption`, `SortOptions`, `IsEmpty`, `EmptyStateVisibility`, `ListVisibility`
  - `[RelayCommand]` for: `ToggleFavoriteCommand(VideoModel)`, `SortCommand(string)`, `PlayFromFavoriteCommand(VideoModel)`, `RemoveFavoriteCommand(VideoModel)`
  - Constructor DI: `PlayerViewModel`, `BilibiliClient`, `CacheService`
  - **Target file**: `ViewModels/LibraryViewModel.cs`

- [x] **L2** Implement favorites persistence  
       Use `ApplicationData.LocalFolder` + JSON file (`favorites.json`)  
       Load on ViewModel init, save on every add/remove  
       Uses `System.Text.Json` for `VideoModel` serialization  
       Auto-loads via `LoadFavoritesAsync()` in constructor

- [x] **L3** Wire `FavoritesPage.xaml` to `LibraryViewModel`  
       Changed ViewModel from `MainViewModel` → `LibraryViewModel`  
       DataTemplate uses `VideoModel` (cover, title, artist, duration, views)  
       `ItemClick` → `PlayFromFavoriteCommand`  
       `ComboBox` sort → `SortCommand`  
       Remove button per row → `RemoveFavoriteCommand`  
       Added `StringToImageSourceConverter` for cover URL→BitmapImage  
       Added empty state UI with visibility binding  
       Removed unused `TrackItem`-based main page binding

- [x] **L4** Add auto-download trigger on favorite  
       When `ToggleFavoriteCommand` adds a video → check `CacheService.IsCachedAsync` → if not cached, call `BilibiliClient.FetchVideoInfoAsync` + `FetchPlayUrlAsync` → `CacheService.DownloadInBackground`

---

### 5. 📦 Caching

| #   | Feature                          | Mobile                       | Windows             | Status | Notes                          |
| --- | -------------------------------- | ---------------------------- | ------------------- | ------ | ------------------------------ |
| 5.1 | Audio file caching (.m4s)        | ✅                           | ❌                  | ❌     |                                |
| 5.2 | Download progress tracking       | ✅ `DownloadProgress` stream | ❌                  | ❌     |                                |
| 5.3 | Background download              | ✅ `downloadInBackground()`  | ❌                  | ❌     |                                |
| 5.4 | Atomic file rename (.tmp → .m4s) | ✅                           | ❌                  | ❌     |                                |
| 5.5 | Cache size calculation           | ✅ formatted string          | ❌                  | ❌     |                                |
| 5.6 | Clear cache                      | ✅                           | ⚠️ static UI button | ⚠️     | UI mock exists in SettingsPage |

#### Implementation Tasks — Caching

- [x] **C1** Create `CacheService`
  - Cache directory: `ApplicationData.LocalFolder\audio_cache\`
  - `GetAudioPathAsync(bvid)` → `string?` (file path if cached)
  - `IsCachedAsync(bvid)` → `bool`
  - `DownloadAudioAsync(url, bvid, progress)` → downloads to `.tmp`, atomic rename to `{bvid}.m4s`
  - `DownloadInBackground(url, bvid)` → fire-and-forget with progress events
  - `GetFormattedCacheSizeAsync()` → `string` (e.g., "42.9 MB")
  - `ClearCacheAsync()`
  - Progress: `EventHandler<DownloadProgress>` event-based
  - **Target file**: `Services/CacheService.cs`
  - **Register** as singleton in DI

- [x] **C2** Create `DownloadProgress` model  
       Properties: `Bvid`, `ReceivedBytes`, `TotalBytes`, `Progress` (0.0-1.0), `IsComplete`, `Error`  
       **Target file**: `Models/DownloadProgress.cs`

- [x] **C3** Wire `CacheService` to `SettingsViewModel`
  - `ClearCacheCommand` → calls `CacheService.ClearCacheAsync()`
  - `CacheSize` property → calls `CacheService.GetFormattedCacheSizeAsync()` on load

---

### 6. ⚙️ Settings

| #   | Feature                      | Mobile                     | Windows                   | Status | Notes                                               |
| --- | ---------------------------- | -------------------------- | ------------------------- | ------ | --------------------------------------------------- |
| 6.1 | Theme mode (Auto/Light/Dark) | ✅ segmented button + Hive | ✅ RadioButtons + binding | ✅     | Theme switching via FrameworkElement.RequestedTheme |
| 6.2 | Fade playback toggle         | ✅ Switch + Hive           | ✅ ToggleSwitch + binding | ✅     | Synced to PlayerViewModel.EnableFade                |
| 6.3 | Auto-play toggle             | ✅                         | ✅ ToggleSwitch + binding | ✅     | Persisted to LocalSettings                          |
| 6.4 | Audio quality selector       | ✅ (mock)                  | ✅ bound to VM property   | ✅     | Read-only display                                   |
| 6.5 | Clear cache                  | ✅ with size display       | ✅ with real CacheService | ✅     | Shows formatted size + clears cache                 |

#### Implementation Tasks — Settings

- [x] **ST1** Create `SettingsViewModel` (enhanced)
  - `[ObservableProperty]` for: `ThemeIndex` (0=System/1=Light/2=Dark), `EnableFade`, `EnableAutoPlay`, `AudioQuality`, `CacheSize`
  - `[RelayCommand]` for: `ClearCacheCommand`
  - ✅ Persist settings to `ApplicationData.LocalSettings`
  - ✅ Load settings on initialization (`LoadSettings()`)
  - ✅ Sync `EnableFade` with `PlayerViewModel`
  - ✅ Constructor DI: `CacheService` + `PlayerViewModel`
  - **Target file**: `ViewModels/SettingsViewModel.cs`

- [x] **ST2** Wire `SettingsPage.xaml` to `SettingsViewModel`
  - ✅ Theme selector: `RadioButtons SelectedIndex="{x:Bind ViewModel.ThemeIndex, Mode=TwoWay}"`
  - ✅ Fade ToggleSwitch → bound to `ViewModel.EnableFade`
  - ✅ Auto-play ToggleSwitch → bound to `ViewModel.EnableAutoPlay`
  - ✅ Cache size → bound to `ViewModel.CacheSize`
  - ✅ Clear cache button → bound to `ViewModel.ClearCacheCommand`
  - ✅ `AutomationProperties.AutomationId` on all controls

- [x] **ST3** Implement theme switching  
       When `ThemeMode` changes:
  - ✅ `System`: `ElementTheme.Default` (follow OS)
  - ✅ `Light`: `ElementTheme.Light`
  - ✅ `Dark`: `ElementTheme.Dark`
  - ✅ Apply via `FrameworkElement.RequestedTheme` on `MainWindow.Content`
  - ✅ `MainWindow` registers via `SettingsViewModel.SetMainWindow(this)` in constructor
  - ✅ Changed `SettingsViewModel` registration from `Transient` → `Singleton` in DI

---

### 7. 🎨 UI / Theming

| #   | Feature                      | Mobile                    | Windows                       | Status | Notes                                                           |
| --- | ---------------------------- | ------------------------- | ----------------------------- | ------ | --------------------------------------------------------------- | --- |
| 7.1 | Light/Dark/System theme      | ✅ Material 3             | ✅ RadioButtons + Apply       | ✅     | ST3 done: System/Light/Dark via FrameworkElement.RequestedTheme |
| 7.2 | Brand colors (Bilibili pink) | ✅ `#FB7299`              | ✅ ThemeResource brushes      | ✅     | U2 done: LightColors/DarkColors with theme-aware Rose/Slate     |
| 7.3 | Typography styles            | ✅ Material 3 text styles | ✅ WinUI typography           | ✅     | U3 done: Title/Subtitle/Caption styles replaced raw FontSize    |     |
| 7.4 | Mica/Backdrop effect         | N/A                       | ✅ MicaBackdrop               | ✅     | U4 done: SystemBackdrop = new MicaBackdrop()                    |     |
| 7.5 | TitleBar customization       | N/A                       | ✅ ExtendsContentIntoTitleBar | ✅     | U5 done: SetTitleBar(AppTitleBar) with custom drag region       |     |
| 7.6 | NavigationView sidebar       | N/A (bottom nav)          | ✅ NavigationView             | ✅     | U1 done: LeftCompact mode with MenuItems + Frame navigation     |

#### Implementation Tasks — UI

- [x] **U1** Replace custom sidebar with `NavigationView`
  - `NavigationView.PaneDisplayMode="LeftCompact"`
  - Items: Search (Audio), Library (Favorites), Settings (footer)
  - `NavigationView.SelectionChanged` → navigate Frame
  - Removed manual `ListView` sidebar from `MainWindow.xaml`

- [x] **U2** Add theme-aware resource dictionaries
  - `Themes/LightColors.xaml` — light theme brushes (Rose + Slate scale)
  - `Themes/DarkColors.xaml` — dark theme brushes (inverted Slate scale)
  - `App.xaml` uses `ResourceDictionary.ThemeDictionaries` to merge
  - All `{StaticResource Slate/Rose...}` migrated to `{ThemeResource Slate/Rose...}`

- [x] **U3** Replace raw `FontSize`/`FontWeight` with WinUI typography styles
  - `TitleTextBlockStyle`, `SubtitleTextBlockStyle`, `BodyTextBlockStyle`, `CaptionTextBlockStyle` applied across all pages

- [x] **U4** Add `MicaBackdrop` to `MainWindow`
  - `SystemBackdrop = new MicaBackdrop()`

- [x] **U5** Customize title bar
  - `ExtendsContentIntoTitleBar = true`
  - `SetTitleBar(AppTitleBar)` with custom drag region Grid
  - NavigationView respects `IsTitleBarAutoPaddingEnabled="False"`

- [x] **U6** Add `AutomationProperties.AutomationId` to ALL interactive controls  
       Every Button, TextBox, ListView, ToggleSwitch, NavigationViewItem, Slider: unique AutomationId

---

### 8. 🎬 Video Preview / Detail

| #   | Feature                    | Mobile                    | Windows                               | Status | Notes                               |
| --- | -------------------------- | ------------------------- | ------------------------------------- | ------ | ----------------------------------- |
| 8.1 | Video preview dialog       | ✅ media_kit video player | ✅ ContentDialog + MediaPlayerElement | ✅     |                                     |
| 8.2 | Stats bar                  | ✅ full page with stats   | ✅ VideoStatsBar (UserControl)        | ✅     | 5 stats: 播放/点赞/收藏数/硬币/时长 |
| 8.3 | Wakelock (screen stay on)  | ✅ `wakelock_plus`        | N/A                                   | N/A    | Desktop doesn't need                |
| 8.4 | Pause audio on video entry | ✅                        | ✅                                    | ✅     | V2 done: PreviewVideoCommand        |

#### Implementation Tasks — Video

- [x] **V1** Move video preview logic from code-behind → `SearchViewModel`
  - `PreviewVideoCommand` — pauses audio + fills preview info
  - `PreviewTitle`, `PreviewAuthor`, `PreviewMeta`, `PreviewViews`, `PreviewDuration`, `PreviewLikes`, `PreviewDescription`, `PreviewCoverUrl`, `IsPreviewFavorited`, `PreviewFavoriteText` as observable properties
  - `TogglePreviewFavoriteCommand` — MVVM favorite toggle in preview
  - `ClosePreviewCommand` — MVVM close button
  - XAML updated: `x:Name` → `{x:Bind ViewModel.XXX, Mode=OneWay}`
  - Code-behind cleaned: removed `UpdatePreviewInfo`, `OnToggleFavoriteClick`, `UpdateFavoriteButtonState`
  - `_libraryViewModel` field removed from `SearchPage.xaml.cs`

- [x] **V2** When video preview opens, pause audio playback (if playing)  
       ✅ `PlayerViewModel.PauseCommand.Execute(null)` called inside `PreviewVideoCommand`

- [x] **V3** Create `VideoStatsBar` reusable UserControl (replaces deleted `VideoDetailPage`)  
       ✅ `Controls/VideoStatsBar.xaml` + `.xaml.cs` — 5-column stats: 播放/点赞/收藏数/硬币/时长  
       ✅ Removed 弹幕, added 时长 after 硬币  
       ✅ 5 `DependencyProperty` for data binding: `ViewCount`, `LikeCount`, `FavoriteCount`, `CoinCount`, `Duration`  
       ✅ Integrated into SearchPage preview panel, replacing old 3-column inline stats  
       ✅ Added `PreviewFavoriteCount` and `PreviewCoinCount` to `SearchViewModel`  
       ✅ VideoDetailPage deleted (both .xaml and .xaml.cs)

---

### 9. 🧹 Code Quality (from Code Review)

- [x] **Q1** Replace all `async void` event handlers with `[RelayCommand]` bindings  
       SearchPage: `OnSearchKeyDown`, `OnKeywordClick`, `OnClearHistoryClick`, `OnVideoClick`  
       Use `Command` + `CommandParameter` in XAML instead  
       ✅ Done: `OnSearchKeyDown`/`OnKeywordClick` → synchronous delegates that call `SearchCommand.Execute()`; `OnClearHistoryClick` → replaced with `Command="{x:Bind ViewModel.ClearHistoryCommand}"` in XAML; `OnVideoClick` kept as event handler (dialog coordination per MVVM).

- [x] **Q2** Fix `HttpRangeStream.Read()` — remove `GetAwaiter().GetResult()`  
       ✅ Done in P0.6

- [x] **Q3** Add `NavigationCacheMode="Disabled"` to SearchPage (heavy resources)  
       ✅ Done.

- [x] **Q4** Remove hardcoded seed data from `MainViewModel.Seed()` — load from persistence  
       ✅ Done: `Seed()` method removed, constructor no longer calls it.

- [x] **Q5** Consolidate duplicate HTTP header setup  
       `BilibiliClient.cs` and `SearchPage.xaml.cs` both set User-Agent/Referer headers  
       Move to a shared `HttpClientFactory` or `BilibiliClient` static constructor  
       ✅ Done: Added `BilibiliClient.ConfigureDefaultHeaders(HttpClient)` static method; `App.xaml.cs` and `BilibiliClient` constructor now use it.

- [x] **Q6** Add `using` statements to all disposable references  
       Verify `JsonDocument`, `HttpResponseMessage`, `Stream` all properly disposed  
       ✅ Done: `BilibiliClient` now implements `IDisposable` (disposes `_initLock` and `_client`). `JsonDocument`/`HttpResponseMessage` already used with `using` statements.

- [x] **Q7** Remove `PreviewHttpClient` static field — use singleton from DI  
       ⚠️ P0.3 registered a shared `HttpClient` in DI; `SearchPage.xaml.cs` still creates its own `PreviewHttpClient`.  
       ✅ Done: `PreviewHttpClient` removed; `SearchPage` now injects DI `HttpClient`; `HttpRangeStream` sets `Accept-Encoding: identity` per-request.

- [x] **Q8** Add `[RelayCommand]` to `MainViewModel.SearchAsync` and `ClearHistoryAsync`  
       ⚠️ **Deferred from P0.4**: `[RelayCommand]` makes the annotated method private (the source generator creates a public `IRelayCommand` property instead). `SearchPage.xaml.cs` currently calls `ViewModel.SearchAsync(keyword)` and `ViewModel.ClearHistoryAsync()` directly. These call-sites must be updated to use `ViewModel.SearchCommand.Execute(keyword)` and `ViewModel.ClearHistoryCommand.Execute(null)` — coordinate with Q1 (replace `async void` handlers with Command bindings).  
       **Target file**: `ViewModels/MainViewModel.cs`  
       ✅ Done: Both methods now use `[RelayCommand]`; call-sites updated to use `SearchCommand.Execute()` / `ClearHistoryCommand.Execute()`.

---

## ⚠️ Deferral Rule

> **When a task cannot be fully completed in the current phase, the deferred work MUST be recorded in the target phase's task list as a new checkbox item with `⚠️ Deferred from Phase X:` prefix. This ensures no work is silently dropped between phases.**

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
Phase 9  ST1..ST3 (settings VM + theme switching) ← depends on C  ✅
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
