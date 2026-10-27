using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Api;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace bilibili_music_player_windows.Services;

/// <summary>Each source owns its player, seekable stream and events. Old callbacks are ignored.</summary>
public sealed class AudioPlayerService : IAudioPlaybackEngine, IDisposable
{
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(250));
    private readonly Task _positionUpdates;
#if DEBUG
    private long _seekCount;
    internal object Inspect()
    {
        lock (_gate)
            return new { position = _current?.Player.PlaybackSession.Position.TotalSeconds,
                state = _current?.Player.PlaybackSession.PlaybackState.ToString(),
                updates = _positionUpdates.Status.ToString(), error = _positionUpdates.Exception?.ToString(), seeks = _seekCount };
    }
#endif
    private Session? _current;
    private CancellationTokenSource? _fade;
    private long _version;
    private double _volume = .8;
    private bool _disposed;
    public event EventHandler<AudioPlaybackSnapshot>? Changed;

    public AudioPlayerService(HttpClient http)
    { _http = http; _positionUpdates = UpdatePositionAsync(); }

    public double Volume
    {
        get { lock (_gate) return _volume; }
        set
        {
            lock (_gate)
            {
                _fade?.Cancel();
                _volume = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
                if (_current is { } session) session.Player.Volume = _volume;
            }
        }
    }

    public async Task OpenAsync(Uri uri, long sourceId, CancellationToken ct)
    {
        var version = Interlocked.Increment(ref _version);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var session = new Session(sourceId);
        try
        {
            if (uri.IsFile) session.Source = MediaSource.CreateFromUri(uri);
            else
            {
                session.Stream = new HttpRangeStream(_http, uri);
                await session.Stream.InitializeAsync(linked.Token).ConfigureAwait(false);
                session.RandomAccess = session.Stream.AsRandomAccessStream();
                var type = session.Stream.ContentType;
                session.Source = MediaSource.CreateFromStream(session.RandomAccess,
                    type is "application/octet-stream" or "" ? "audio/mp4" : type);
            }
            linked.Token.ThrowIfCancellationRequested();
            session.Player.MediaOpened += session.OpenedHandler = (_, _) =>
            { session.Opened.TrySetResult(); Publish(session); };
            session.Player.MediaFailed += session.FailedHandler = (_, args) =>
            {
                var message = string.IsNullOrWhiteSpace(args.ErrorMessage) ? "音频无法解码或地址已失效，请重试。" : args.ErrorMessage;
                session.Opened.TrySetException(new InvalidOperationException(message));
                Publish(session, error: message);
                Release(session);
            };
            session.Player.MediaEnded += session.EndedHandler = (_, _) => Publish(session, ended: true);
            session.Player.PlaybackSession.PlaybackStateChanged += session.StateHandler = (_, _) => Publish(session);
            session.Player.PlaybackSession.NaturalDurationChanged += session.DurationHandler = (_, _) => Publish(session);
            session.Player.PlaybackSession.PositionChanged += session.PositionHandler = (_, _) => Publish(session);
            session.Player.PlaybackSession.SeekCompleted += session.SeekHandler = (_, _) => Publish(session);
            Session? previous;
            lock (_gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (version != _version) throw new OperationCanceledException(linked.Token);
                previous = _current; _current = session;
                session.Player.Volume = _volume;
                session.Player.Source = session.Source;
            }
            previous?.Dispose();
            await session.Opened.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
        }
        catch { Release(session); throw; }
    }

    public void Resume() { lock (_gate) _current?.Player.Play(); }
    public void Pause() { lock (_gate) _current?.Player.Pause(); }
    public void Seek(TimeSpan position)
    {
        Session? session;
        lock (_gate)
        {
            session = _current;
            if (session is null || !session.Player.PlaybackSession.CanSeek) return;
#if DEBUG
            _seekCount++;
#endif
            var duration = session.Player.PlaybackSession.NaturalDuration;
            session.Player.PlaybackSession.Position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks));
        }
        Publish(session);
    }
    public void Stop()
    {
        Interlocked.Increment(ref _version);
        Session? old;
        lock (_gate) { _fade?.Cancel(); old = _current; _current = null; }
        old?.Dispose();
    }
    private void Release(Session session)
    {
        lock (_gate) if (ReferenceEquals(_current, session)) { _fade?.Cancel(); _current = null; }
        session.Dispose();
    }

    public async Task FadeToAsync(double volume, TimeSpan duration, CancellationToken ct)
    {
        using var fade = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        Session? session;
        double start;
        lock (_gate)
        {
            _fade?.Cancel(); _fade = fade;
            session = _current;
            if (session is null) { _fade = null; return; }
            start = session.Player.Volume;
        }
        var target = Math.Clamp(volume, 0, 1);
        var elapsed = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                var fraction = duration <= TimeSpan.Zero ? 1 : Math.Clamp(elapsed.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
                lock (_gate)
                {
                    fade.Token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(session, _current)) return;
                    session.Player.Volume = target == 0 ? start * (1 - fraction * fraction) : start + (target - start) * fraction;
                    if (fraction >= 1) { _volume = target; return; }
                }
                await Task.Delay(20, fade.Token).ConfigureAwait(false);
            }
        }
        finally { lock (_gate) if (ReferenceEquals(_fade, fade)) _fade = null; }
    }

    private async Task UpdatePositionAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
            {
                Session? session;
                lock (_gate) session = _current;
                if (session is not null) Publish(session);
            }
        }
        catch (OperationCanceledException) { }
    }
    private void Publish(Session session, bool ended = false, string? error = null)
    {
        AudioPlaybackSnapshot snapshot;
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(session, _current)) return;
            if (error is not null) snapshot = new(session.Id, TimeSpan.Zero, TimeSpan.Zero, false, false, false, Error: error);
            else
            {
                try
                {
                    var state = session.Player.PlaybackSession;
                    var playbackState = state.PlaybackState;
                    // BufferingProgress is unsupported by some stream-backed
                    // sessions (E_NOINTERFACE/InvalidCastException). Buffering
                    // UI needs only PlaybackState; never poll that optional API.
                    snapshot = new(session.Id, state.Position, state.NaturalDuration,
                        playbackState == MediaPlaybackState.Playing,
                        playbackState is MediaPlaybackState.Opening or MediaPlaybackState.Buffering,
                        state.CanSeek, Ended: ended);
                }
                // The native session can reject a read during a transition.
                // Skip this sample, rather than losing all subsequent updates
                // or throwing out of a native PositionChanged callback.
                catch (ObjectDisposedException) { return; }
                catch (COMException ex) { Debug.WriteLine($"[Audio position] {ex.Message}"); return; }
                catch (InvalidCastException ex) { Debug.WriteLine($"[Audio position] {ex.Message}"); return; }
            }
        }
        Changed?.Invoke(this, snapshot);
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Cancel(); _timer.Dispose(); Stop(); _lifetime.Dispose();
    }

    private sealed class Session(long id) : IDisposable
    {
        public long Id { get; } = id;
        public MediaPlayer Player { get; } = new() { AutoPlay = false, AudioCategory = MediaPlayerAudioCategory.Media };
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MediaSource? Source;
        public HttpRangeStream? Stream;
        public IRandomAccessStream? RandomAccess;
        public Windows.Foundation.TypedEventHandler<MediaPlayer, object>? OpenedHandler, EndedHandler;
        public Windows.Foundation.TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs>? FailedHandler;
        public Windows.Foundation.TypedEventHandler<MediaPlaybackSession, object>? StateHandler, DurationHandler, PositionHandler, SeekHandler;
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Player.MediaOpened -= OpenedHandler; Player.MediaEnded -= EndedHandler; Player.MediaFailed -= FailedHandler;
            Player.PlaybackSession.PlaybackStateChanged -= StateHandler;
            Player.PlaybackSession.NaturalDurationChanged -= DurationHandler;
            Player.PlaybackSession.PositionChanged -= PositionHandler;
            Player.PlaybackSession.SeekCompleted -= SeekHandler;
            Opened.TrySetCanceled();
            Player.Pause(); Player.Source = null; Player.Dispose();
            Source?.Dispose(); RandomAccess?.Dispose(); Stream?.Dispose();
        }
    }
}
