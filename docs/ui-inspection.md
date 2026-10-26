# Windows UI layout inspection

Debug builds expose a current-user-only named pipe, `BiliMusic.Ui.<process id>`.
Release builds do not include this channel. The application must be running, but
inspection does not move the mouse, type into the desktop, or require screenshots.

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
