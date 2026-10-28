#if DEBUG
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Foundation;
using Windows.Graphics;
using bilibili_music_player_windows.Controls;
using bilibili_music_player_windows.Diagnostics;

namespace bilibili_music_player_windows;

public sealed partial class MainWindow
{
    private Canvas? _inspectionReferenceOverlay;
    private WindowActivationState _inspectionActivationState = WindowActivationState.Deactivated;
    // A local, current-user-only inspection channel. It is absent from Release
    // builds and never changes saved preferences or executes arbitrary code.
    private void StartUiDiagnostics()
    {
        var shutdown = new CancellationTokenSource();
        Closed += (_, _) => shutdown.Cancel();
        Activated += (_, args) => _inspectionActivationState = args.WindowActivationState;
        _ = Task.Run(async () =>
        {
            while (!shutdown.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream($"BiliMusic.Ui.{Environment.ProcessId}",
                        PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(shutdown.Token);
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                    var line = await reader.ReadLineAsync(shutdown.Token);
                    if (line is null || line.Length > 8192) continue;
                    var request = JsonSerializer.Deserialize<UiInspectionRequest>(line,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                    var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (!DispatcherQueue.TryEnqueue(async () =>
                    {
                        try { completion.TrySetResult(JsonSerializer.Serialize(await InspectUiAsync(request),
                            new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals })); }
                        catch (Exception ex) { completion.TrySetResult(JsonSerializer.Serialize(new { error = ex.Message })); }
                    })) break;
                    await writer.WriteLineAsync(await completion.Task.WaitAsync(shutdown.Token));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[UI inspection] {ex.Message}"); }
            }
        });
    }

    private async Task<object> InspectUiAsync(UiInspectionRequest request)
    {
        if (request.Activate) ShowFromBackground();
        if (request.LibraryFixture is not null) await _libraryViewModel.InspectFixtureAsync(request.LibraryFixture);
        await InspectPlaybackAsync(request);
        var root = (FrameworkElement)Content;
        if (_inspectionReferenceOverlay is not null)
        {
            ((Panel)root).Children.Remove(_inspectionReferenceOverlay);
            _inspectionReferenceOverlay = null;
        }
        if (request.Tab is not null)
        {
            if (request.Tab is not ("search" or "favorites" or "settings" or "nowplaying" or "playlist"))
                throw new ArgumentException("Unknown tab.");
            NavigateTo(request.Tab);
        }
        if (request.Theme is not null)
            root.RequestedTheme = request.Theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                "Default" => ElementTheme.Default,
                _ => throw new ArgumentException("Unknown temporary theme."),
            };
        if (request.Back) TryGoBack();
        if (request.Width is { } width && request.Height is { } height)
        {
            if (width is < 320 or > 3840 || height is < 320 or > 2160)
                throw new ArgumentException("Inspection dimensions are outside supported bounds.");
            // AppWindow uses physical pixels; the protocol uses logical DIPs.
            var scale = root.XamlRoot?.RasterizationScale ?? 1;
            AppWindow.Resize(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
        }
        root.UpdateLayout();
        await Task.Delay(100);
        if (UiLayoutSnapshot.Find(root, "TransportTime") is TextBlock time)
        {
            if (request.TimeLineBounds is { } lineBounds)
                time.TextLineBounds = lineBounds switch
                {
                    "Full" => TextLineBounds.Full,
                    "TrimToCapHeight" => TextLineBounds.TrimToCapHeight,
                    "TrimToBaseline" => TextLineBounds.TrimToBaseline,
                    "Tight" => TextLineBounds.Tight,
                    _ => throw new ArgumentException("Unsupported time text line bounds."),
                };
        }
        var search = UiLayoutSnapshot.Find(root, "SearchTextBox") as SearchTextBox;
        if (request.InputFont is not null && search is not null)
        {
            if (request.InputFont is not ("Microsoft YaHei" or "Microsoft YaHei UI" or "XamlAutoFontFamily"))
                throw new ArgumentException("Unsupported inspection font.");
            search.FontFamily = new FontFamily(request.InputFont);
        }
        if (request.Text is not null && search is not null) search.Text = request.Text;
        if (request.SelectionStart is { } selection && search is not null)
            search.Select(Math.Clamp(selection, 0, search.Text.Length), 0);
        if (request.Focus == "search") search?.Focus(FocusState.Keyboard);
        else if (request.Focus == "navigation") ((Control)NavView.MenuItems[1]).Focus(FocusState.Programmatic);
        await Task.Delay(80);
        root.UpdateLayout();
        if (request.State is not null)
        {
            if (request.State is not ("Normal" or "PointerOver" or "Pressed" or "Focused" or "Disabled"))
                throw new ArgumentException("Unknown visual state.");
            var target = UiLayoutSnapshot.Find(root, request.Target ?? "SearchTextBox") as Control
                ?? throw new ArgumentException("Visual state target not found.");
            if (!VisualStateManager.GoToState(target, request.State, false))
                throw new InvalidOperationException("Target has no requested visual state.");
            await Task.Delay(50);
        }
        root.UpdateLayout();
        var snapshot = UiLayoutSnapshot.Capture(root);
        bool? protectedSessionRoundTrip = null;
        if (request.CheckProtection)
        {
            const string fixture = "BiliMusic session protection fixture";
            var clear = Windows.Security.Cryptography.CryptographicBuffer.ConvertStringToBinary(fixture,
                Windows.Security.Cryptography.BinaryStringEncoding.Utf8);
            var encrypted = await new Windows.Security.Cryptography.DataProtection.DataProtectionProvider("LOCAL=user").ProtectAsync(clear);
            var decrypted = await new Windows.Security.Cryptography.DataProtection.DataProtectionProvider().UnprotectAsync(encrypted);
            protectedSessionRoundTrip = Windows.Security.Cryptography.CryptographicBuffer.ConvertBinaryToString(
                Windows.Security.Cryptography.BinaryStringEncoding.Utf8, decrypted) == fixture;
        }
        object? nativeComparison = null;
        Canvas? visibleOverlay = null;
        if ((request.CompareNative || request.VisibleReference) && search is not null)
        {
            // The reference uses the SDK's untouched TextBox template and the
            // same effective dimensions/font. It never enters keyboard focus.
            var overlay = new Canvas { Opacity = request.VisibleReference ? 1 : 0, IsHitTestVisible = request.VisibleReference };
            var reference = new TextBox
            {
                Name = "NativeTextBoxReference", Width = search.ActualWidth, Height = search.ActualHeight,
                FontFamily = search.FontFamily, FontSize = search.FontSize,
                Text = search.Text, PlaceholderText = search.PlaceholderText,
            };
            var host = (Panel)root;
            host.Children.Add(overlay);
            var keepForCapture = false;
            try
            {
                overlay.Children.Add(reference);
                if (request.VisibleReference)
                {
                    var anchor = search.TransformToVisual(root).TransformPoint(new Point());
                    Canvas.SetLeft(reference, anchor.X);
                    Canvas.SetTop(reference, anchor.Y + 64);
                }
                reference.ApplyTemplate();
                root.UpdateLayout();
                await Task.Delay(60); // Let native Loaded/validation states initialize.
                reference.Measure(new Size(search.ActualWidth, search.ActualHeight));
                reference.Arrange(new Rect(Canvas.GetLeft(reference) is var left && !double.IsNaN(left) ? left : 0,
                    Canvas.GetTop(reference) is var top && !double.IsNaN(top) ? top : 0, search.ActualWidth, search.ActualHeight));
                reference.UpdateLayout();
                if (request.VisibleReference)
                {
                    reference.Select(search.SelectionStart, search.SelectionLength);
                    if (request.FocusReference) reference.Focus(FocusState.Keyboard);
                    await Task.Delay(100);
                    root.UpdateLayout();
                    snapshot = UiLayoutSnapshot.Capture(root);
                }
                else
                {
                    var state = UiLayoutSnapshot.CommonState(search);
                    if (state is not null) VisualStateManager.GoToState(reference, state, false);
                }
                nativeComparison = new { actual = UiLayoutSnapshot.Capture(search), reference = UiLayoutSnapshot.Capture(reference) };
                keepForCapture = request.VisibleReference && (request.Capture || request.HoldReference);
                if (keepForCapture) visibleOverlay = overlay;
                if (request.HoldReference && keepForCapture) _inspectionReferenceOverlay = overlay;
            }
            finally { if (!keepForCapture) host.Children.Remove(overlay); }
        }
        string? png = null;
        if (request.Capture)
        {
            var bitmap = new RenderTargetBitmap();
            var previousClip = root.Clip;
            try
            {
                // Capture the window's stable layout frame. Capturing just the
                // editor can include shifted transparent descendant bounds and
                // change PNG dimensions even though the control stays fixed.
                root.Clip = new RectangleGeometry { Rect = new Rect(0, 0, root.ActualWidth, root.ActualHeight) };
                await bitmap.RenderAsync(root);
            }
            finally
            {
                root.Clip = previousClip;
                if (visibleOverlay is not null && !request.HoldReference) ((Panel)root).Children.Remove(visibleOverlay);
            }
            var buffer = await bitmap.GetPixelsAsync();
            var pixels = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
            await encoder.FlushAsync();
            stream.Seek(0);
            var encoded = new byte[(int)stream.Size];
            using (var reader = new DataReader(stream))
            {
                await reader.LoadAsync((uint)encoded.Length);
                reader.ReadBytes(encoded);
            }
            png = Convert.ToBase64String(encoded);
        }
        return new { snapshot, nativeComparison, png, protectedSessionRoundTrip, activation = _inspectionActivationState.ToString(), savedTheme = _settingsViewModel.ThemeMode,
            playerBackground = (_tabs?.ActivePage as Pages.AudioPlayerPage)?.InspectBackground(),
            lifetime = new { inBackground = _isInBackground, visible = AppWindow.IsVisible, trayAvailable = _tray?.IsAvailable,
                activeTab = _tabs?.ActiveKey, backEnabled = TitleBackButton.IsEnabled, searchStage = _searchViewModel.Stage.ToString() },
            playback = new { title = _playerViewModel.TrackTitle, bvid = _playerViewModel.CurrentTrack?.Bvid,
                playing = _playerViewModel.IsPlaying, loading = _playerViewModel.IsLoading, buffering = _playerViewModel.IsBuffering,
                position = _playerViewModel.Position.TotalSeconds, duration = _playerViewModel.Duration.TotalSeconds,
                progress = _playerViewModel.Progress, canSeek = _playerViewModel.CanSeek, error = _playerViewModel.ErrorMessage,
                index = _playerViewModel.CurrentIndex, count = _playerViewModel.QueueCount, volume = _playerViewModel.Volume,
                time = _playerViewModel.TimeText, seeking = _playerViewModel.IsUserSeeking,
                mode = _playerViewModel.PlayMode.ToString(), sourceId = _playerViewModel.InspectionSourceId,
                queue = _playerViewModel.Playlist.Select(e => new { bvid = e.Track.Bvid, title = e.Track.Title,
                    sequence = e.SequenceText, current = e.IsCurrent, dimmed = e.IsDimmed, playing = e.IsPlaying }).ToArray(),
                engine = App.Current.GetService<Services.AudioPlayerService>().Inspect() },
            library = new { ready = _libraryViewModel.IsReady, kind = _libraryViewModel.CurrentKind,
                currentId = _libraryViewModel.CurrentId, nodes = _libraryViewModel.Document.Nodes.Count,
                media = _libraryViewModel.Document.Media.Count, entries = _libraryViewModel.Document.Entries.Count,
                likes = _libraryViewModel.Document.Likes.Count, canGoBack = _libraryViewModel.CanGoBack,
                signedIn = _libraryViewModel.IsSignedIn, status = _libraryViewModel.SyncStatus },
            searchTextStart = search is null ? (double?)null : search.TransformToVisual(root).TransformPoint(new Point(search.TextStartOffset, 0)).X,
            requestedTheme = root.RequestedTheme.ToString(), windowSize = new { width = AppWindow.Size.Width, height = AppWindow.Size.Height } };
    }
}

internal sealed class UiInspectionRequest
{
    public bool Back { get; set; }
    public string? TimeLineBounds { get; set; }
    public string? PlaybackFixture { get; set; }
    public string? PlaybackAction { get; set; }
    public string? PlaybackBvid { get; set; }
    public double? PlaybackProgress { get; set; }
    public double? PlaybackPreview { get; set; }
    public string? LibraryFixture { get; set; }
    public bool CheckProtection { get; set; }
    public string? Tab { get; set; }
    public string? Theme { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string? Text { get; set; }
    public string? Focus { get; set; }
    public string? State { get; set; }
    public string? Target { get; set; }
    public bool CompareNative { get; set; }
    public bool Activate { get; set; }
    public bool Capture { get; set; }
    public int? SelectionStart { get; set; }
    public bool VisibleReference { get; set; }
    public bool FocusReference { get; set; }
    public bool HoldReference { get; set; }
    public string? InputFont { get; set; }
}
#endif
