# Windows UI layout inspection

Debug builds expose a current-user-only named pipe, `BiliMusic.Ui.<process id>`.
Release builds do not include this channel. The application must be running, but
inspection does not move the mouse, type into the desktop, or require screenshots.

Library, search, playlist and settings share the settings page's layout: `PageContentMargin` is 32 DIPs on
every side and `PageHeaderStyle` leaves 32 DIPs below the header. Each page's
`PageTitle` uses `PageTitleTextBlockStyle` (28 DIP semibold Microsoft YaHei,
`Slate800`), and `AppPageStyle` supplies the same primary foreground for body
content. Do not override heading size, color or outer spacing on individual pages.
The expanded player has no page heading and covers the entire right content area,
including the transport. The sidebar and window title bar remain available.
The shared `PageTitle` automation ID allows native snapshots
to compare heading bounds, baseline and resolved theme colors across pages.
`playerBackground` reports the expanded player's Gaussian effect, decode status
and image-load count. Composition visuals require a real window screenshot for
visual confirmation; RenderTargetBitmap does not capture their pixels reliably.

```powershell
./scripts/Inspect-Ui.ps1 -Tab search -Theme Light -Text '' -Focus navigation -CompareNative -OutputPath ./artifacts/ui/light-empty
./scripts/Inspect-Ui.ps1 -Tab search -Theme Dark -Text '搜索 Bilibili 音乐视频' -Focus search -CompareNative -OutputPath ./artifacts/ui/dark-typed
./scripts/Inspect-Ui.ps1 -State PointerOver -Target SubmitSearchButton -OutputPath ./artifacts/ui/button-hover
./scripts/Inspect-Ui.ps1 -Width 743 -Height 800 -OutputPath ./artifacts/ui/narrow
./scripts/Inspect-Ui.ps1 -Theme Default -Text '' -Focus navigation
```

JSON contains actual XAML bounds, layout slots, padding, margins, effective fonts,
TextBlock baselines, resolved brushes, scroll offsets and visual states. Coordinates
are DIPs; `scale` records the display rasterization scale. Collapsed resident pages
are excluded. `-CompareNative` also measures an untouched native TextBox with the
same size, font, text and visual state, for vertical-layout comparisons.
The search field keeps its native natural height, padding and editor/caret
vertical layout. A 32-DIP prefix button provides search without replacing the
native template. Only the placeholder TextBlock's padding is adjusted: top -1 DIP,
bottom +1 DIP, to align it optically with entered text while preserving its size.
No text layer has a RenderTransform; the editor and caret have no optical offset.

`-VisibleReference -HoldReference` displays an ordinary TextBox below the search
field. `-FocusReference` actually focuses it; this can verify real editing behavior
without confusing a Focused visual state with RichEdit keyboard activation.
The next inspection request removes this temporary reference.
`-Capture` is an offscreen application render, not proof of final screen rendering.

`-Svg` optionally adds a **layout wireframe generated from those measured coordinates**, with
TextBlock baseline guides. It is not a lossless vector copy of native controls,
DirectWrite glyph rasterization, video, shadows or compositor effects. TextBox
character rectangles are recorded without inventing an unavailable glyph baseline.
Converting a screenshot to an SVG image container would not improve precision.

Theme changes through this channel are temporary and do not change saved settings.
Text updates may request the existing public search suggestions; they never submit
a video search. `-State` verifies style states, not real pointer routing. Restore
`-Theme Default`, text, dimensions and focus after an inspection session.

Library QA uses an isolated local profile with cloud synchronization disabled:

```powershell
./scripts/Inspect-Ui.ps1 -Tab favorites -LibraryFixture sample
./scripts/Inspect-Ui.ps1 -Width 470 -Height 800
./scripts/Inspect-Ui.ps1 -LibraryFixture restore -Theme Default
```

The fixture does not edit the actual account or guest library. `library` metadata
reports graph counts, current location and account/sync state without credentials.
Unmeasured virtualized control metrics can be serialized as `"Infinity"` or `"NaN"`;
do not use those metrics for visible-layout assertions.

`-CheckProtection` round-trips a non-secret in-memory fixture through Windows
`LOCAL=user` protection. It does not read or write the user's session file.

`-ProbePlayerTransition` samples a native player sheet opening, closing midway,
and reopening midway, then returns to its starting tab. `transitionProbe` records
the values on both sides of each reversal and the final visibility. Ordinary
inspection requests wait for the transition to settle before exporting geometry.
`settings` reports non-secret preference values without changing them. The settings
layout regression checks both themes at 430, 760 and 1245 DIP; native UI selection
followed by a complete process restart verifies LocalSettings restoration.

`-ProbeTabPresentation settings|playlist|search` observes the pending page before
its first reveal: opacity, previous-page visibility, render-pass count, theme,
and final replacement. Run `Test-TabPresentation.ps1 -Theme Dark` or `-Theme Light`
immediately after a fresh app start so all three target tabs are still cold.
The presenter keeps the paused previous page visible while the next page inherits
its theme and completes a hidden render pass; prepared resident pages switch
immediately. Ordinary diagnostics wait for pending presentation to settle.
