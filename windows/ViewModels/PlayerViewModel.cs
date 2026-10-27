#pragma warning disable MVVMTK0045
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bilibili_music_player_windows.ViewModels;

/// <summary>One audio session shared by every transport view. Late sources cannot take ownership.</summary>
public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlaybackEngine _audio;
    private readonly IAudioSourceResolver _resolver;
    private readonly SynchronizationContext _ui;
    private CancellationTokenSource? _sourceCts, _transitionCts;
    private long _sourceId, _seekSourceId, _handledEndId;
    private bool _ready, _canSeek, _wantsPlay, _transitioning, _disposed;
    private double _audibleVolume = .8;
#if DEBUG
    private IReadOnlyDictionary<string, Uri>? _inspectionSources;
    internal void SetInspectionSources(IReadOnlyDictionary<string, Uri>? sources) => _inspectionSources = sources;
#endif

    [ObservableProperty] private VideoModel? _currentTrack;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isBuffering;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private TimeSpan _position;
    [ObservableProperty] private TimeSpan _duration;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private double _bufferingProgress;
    [ObservableProperty] private double _volume = .8;
    [ObservableProperty] private int _currentIndex = -1;
    [ObservableProperty] private int _queueCount;
    [ObservableProperty] private PlayMode _playMode = PlayMode.Loop;
    [ObservableProperty] private bool _isFading;
    [ObservableProperty] private bool _enableFade = true;
    [ObservableProperty] private bool _isUserSeeking;
    public ObservableCollection<VideoModel> Playlist { get; } = new();

    public bool CanStartPlayback => CurrentTrack is not null || Playlist.Count > 0;
    public bool CanSkip => Playlist.Count > 0;
    public bool CanSeek => _ready && _canSeek && !IsLoading && Duration > TimeSpan.Zero;
    public bool HasTrack => CurrentTrack is not null;
    public bool IsMuted => Volume <= 0;
    public bool ShowPause => (IsLoading || _transitioning) ? _wantsPlay : IsPlaying || (IsBuffering && _wantsPlay);
    public bool IsBusy => IsLoading || IsBuffering;
    public string PlayPauseLabel => ErrorMessage is not null ? "重试播放" : ShowPause ? "暂停" : "播放";
    public string TrackTitle => CurrentTrack?.Title ?? "尚未播放";
    public string TrackSubtitle => ErrorMessage ?? (IsLoading ? "正在加载音频…" : IsBuffering ? "正在缓冲…" :
        CurrentTrack?.Artist ?? "从收藏或歌单选择音乐");
    public string TimeText => $"{FormatTime(IsUserSeeking ? TimeSpan.FromTicks((long)(Duration.Ticks * Progress)) : Position)} / {FormatTime(Duration)}";
    public static string FormatTime(TimeSpan time) => time.TotalHours >= 1
        ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}" : $"{Math.Max(0, (int)time.TotalMinutes):D2}:{Math.Max(0, time.Seconds):D2}";

    public PlayerViewModel(IAudioPlaybackEngine audio, IAudioSourceResolver resolver, SynchronizationContext? context = null)
    {
        _audio = audio; _resolver = resolver;
        _ui = context ?? SynchronizationContext.Current ?? throw new InvalidOperationException("Playback requires a UI synchronization context.");
        _audio.Volume = Volume;
        _audio.Changed += OnAudioChanged;
        Playlist.CollectionChanged += (_, _) => { QueueCount = Playlist.Count; RefreshControls(); };
    }

    [RelayCommand(CanExecute = nameof(CanStartPlayback), AllowConcurrentExecutions = true)]
    private Task TogglePlayAsync() => (IsLoading || _transitioning ? _wantsPlay : ShowPause) ? PauseAsync() : PlayAsync();

    [RelayCommand(CanExecute = nameof(CanStartPlayback), AllowConcurrentExecutions = true)]
    private async Task PlayAsync()
    {
        if (CurrentTrack is null) { if (Playlist.Count > 0) await PlayFromQueueAsync(0); return; }
        if (IsLoading) { _wantsPlay = true; RefreshControls(); return; }
        if (!_ready) { await LoadAsync(CurrentTrack); return; }
        await SetTransportAsync(true);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PauseAsync()
    {
        _wantsPlay = false;
        if (IsLoading) { RefreshControls(); return; }
        await SetTransportAsync(false);
    }

    private async Task SetTransportAsync(bool play)
    {
        _transitionCts?.Cancel(); _transitionCts?.Dispose();
        var transition = _transitionCts = new();
        var id = _sourceId;
        _wantsPlay = play; _transitioning = true;
        RefreshControls();
        try
        {
            if (!_ready) return;
            if (play)
            {
                if (Duration > TimeSpan.Zero && Position >= Duration) _audio.Seek(TimeSpan.Zero);
                _audio.Volume = EnableFade ? 0 : Volume;
                _audio.Resume();
                if (EnableFade) { IsFading = true; await _audio.FadeToAsync(Volume, TimeSpan.FromMilliseconds(300), transition.Token); }
            }
            else
            {
                if (EnableFade && IsPlaying)
                { IsFading = true; await _audio.FadeToAsync(0, TimeSpan.FromMilliseconds(300), transition.Token); }
                if (id == _sourceId && !_wantsPlay) { _audio.Pause(); _audio.Volume = Volume; }
            }
        }
        catch (OperationCanceledException)
        {
            // A volume adjustment can cancel a fade without cancelling the pause intent.
            if (id == _sourceId && ReferenceEquals(_transitionCts, transition) && !_wantsPlay) _audio.Pause();
        }
        catch (Exception ex) { if (id == _sourceId) Fail(ex.Message); }
        finally
        {
            if (id == _sourceId && ReferenceEquals(_transitionCts, transition))
            { IsFading = false; _transitioning = false; RefreshControls(); }
        }
    }

    public Task PlayVideoAsync(VideoModel track)
    {
        CurrentIndex = Playlist.ToList().FindIndex(v => SameTrack(v, track));
        return LoadAsync(track);
    }

    private async Task LoadAsync(VideoModel track, bool autoPlay = true)
    {
        _sourceCts?.Cancel(); _sourceCts?.Dispose(); _transitionCts?.Cancel();
        var lifetime = _sourceCts = new();
        var id = ++_sourceId;
        _ready = _canSeek = false; _wantsPlay = autoPlay; _transitioning = false;
        _audio.Stop();
        CurrentTrack = track; Position = Duration = TimeSpan.Zero; Progress = 0;
        IsPlaying = IsFading = IsBuffering = IsUserSeeking = false; ErrorMessage = null; IsLoading = true;
        RefreshControls();
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        preparation.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            ResolvedAudioSource resolved;
#if DEBUG
            if (_inspectionSources?.TryGetValue(track.Bvid, out var localSource) == true) resolved = new(track, localSource);
            else
#endif
                resolved = await _resolver.ResolveAsync(track, preparation.Token);
            preparation.Token.ThrowIfCancellationRequested();
            if (id != _sourceId || _disposed) return;
            CurrentTrack = resolved.Track; RefreshControls();
            await _audio.OpenAsync(resolved.Uri, id, preparation.Token);
            preparation.Token.ThrowIfCancellationRequested();
            if (id != _sourceId || _disposed) return;
            _ready = true; IsLoading = false;
            RefreshControls();
            await SetTransportAsync(_wantsPlay);
            if (id == _sourceId)
            {
                var cacheToken = lifetime.Token;
                _ = Task.Run(() => CacheSafelyAsync(resolved, cacheToken));
            }
        }
        catch (OperationCanceledException) { if (id == _sourceId && !_disposed) { _audio.Stop(); Fail("音频加载超时，请点击播放重试。"); } }
        catch (Exception ex) { if (id == _sourceId && !_disposed) { _audio.Stop(); Fail(ex.Message); } }
    }

    private async Task CacheSafelyAsync(ResolvedAudioSource source, CancellationToken ct)
    { try { await _resolver.CacheAsync(source, ct); } catch (OperationCanceledException) { } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Audio cache] {ex.Message}"); } }

    public async Task PauseForPreviewAsync()
    {
        if (IsLoading)
        {
            ++_sourceId; _sourceCts?.Cancel(); _transitionCts?.Cancel(); _audio.Stop();
            _ready = _canSeek = _wantsPlay = _transitioning = false;
            IsPlaying = IsLoading = IsBuffering = IsFading = IsUserSeeking = false;
            RefreshControls(); return;
        }
        await PauseAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSkip), AllowConcurrentExecutions = true)]
    private Task NextAsync() => AdvanceAsync(false);

    private Task AdvanceAsync(bool automatic)
    {
        if (Playlist.Count == 0) return Task.CompletedTask;
        var index = automatic && PlayMode == PlayMode.Single && CurrentIndex >= 0 ? CurrentIndex :
            PlayMode == PlayMode.Shuffle && Playlist.Count > 1 ? ShuffleIndex() : (CurrentIndex + 1) % Playlist.Count;
        return LoadQueueAsync(index, automatic || CurrentTrack is null || ShowPause);
    }

    private int ShuffleIndex()
    {
        if (CurrentIndex < 0) return Random.Shared.Next(Playlist.Count);
        var value = Random.Shared.Next(Playlist.Count - 1);
        return value >= CurrentIndex ? value + 1 : value;
    }

    [RelayCommand(CanExecute = nameof(CanSkip), AllowConcurrentExecutions = true)]
    private Task PreviousAsync()
    {
        if (CanSeek && Position.TotalSeconds > 3) { _audio.Seek(TimeSpan.Zero); Position = TimeSpan.Zero; Progress = 0; RefreshControls(); return Task.CompletedTask; }
        return LoadQueueAsync(CurrentIndex > 0 ? CurrentIndex - 1 : Playlist.Count - 1, CurrentTrack is null || ShowPause);
    }

    [RelayCommand]
    private void CyclePlayMode() => PlayMode = (PlayMode)(((int)PlayMode + 1) % 3);

    [RelayCommand(CanExecute = nameof(CanSeek))]
    private void Seek(double progress)
    {
        if (!CanSeek || !double.IsFinite(progress)) return;
        progress = Math.Clamp(progress, 0, 1);
        var target = TimeSpan.FromTicks((long)(Duration.Ticks * progress));
        _audio.Seek(target); Position = target; Progress = progress; RefreshControls();
    }

    public void BeginSeek() { if (!CanSeek) return; _seekSourceId = _sourceId; IsUserSeeking = true; }
    public void PreviewSeek(double progress)
    { if (IsUserSeeking && double.IsFinite(progress)) { Progress = Math.Clamp(progress, 0, 1); OnPropertyChanged(nameof(TimeText)); } }
    public void EndSeek(double progress, bool commit = true)
    {
        if (!IsUserSeeking) return;
        IsUserSeeking = false;
        if (commit && _seekSourceId == _sourceId) Seek(progress);
        else { Progress = Duration > TimeSpan.Zero ? Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds, 0, 1) : 0; RefreshControls(); }
    }

    [RelayCommand]
    private void ToggleMute() => Volume = IsMuted ? _audibleVolume : 0;

    public async Task SetPlaylistAsync(IList<VideoModel> tracks, int startIndex = 0)
    {
        var snapshot = tracks.ToArray();
        if (snapshot.Length == 0) { ClearPlaylist(); return; }
        Playlist.Clear(); foreach (var track in snapshot) Playlist.Add(track);
        await PlayFromQueueAsync(Math.Clamp(startIndex, 0, Playlist.Count - 1));
    }
    public void AddToPlaylist(VideoModel track) => Playlist.Add(track);
    public void Enqueue(IReadOnlyList<VideoModel> tracks, bool next)
    {
        var index = next && CurrentIndex >= 0 ? Math.Min(CurrentIndex + 1, Playlist.Count) : Playlist.Count;
        foreach (var track in tracks) Playlist.Insert(index++, track);
    }
    public void RemoveFromPlaylist(int index)
    {
        if (index < 0 || index >= Playlist.Count) return;
        var current = index == CurrentIndex; var wanted = _wantsPlay;
        Playlist.RemoveAt(index);
        if (Playlist.Count == 0) { ClearPlaylist(); return; }
        if (current) _ = LoadQueueAsync(Math.Min(index, Playlist.Count - 1), wanted);
        else if (index < CurrentIndex) CurrentIndex--;
    }
    private Task LoadQueueAsync(int index, bool autoPlay)
    { CurrentIndex = index; return LoadAsync(Playlist[index], autoPlay); }
    private Task PlayFromQueueAsync(int index) => index >= 0 && index < Playlist.Count ? LoadQueueAsync(index, true) : Task.CompletedTask;

    [RelayCommand]
    public void ClearPlaylist()
    {
        ++_sourceId; _sourceCts?.Cancel(); _transitionCts?.Cancel();
        _audio.Stop(); _ready = _canSeek = _wantsPlay = _transitioning = false;
        Playlist.Clear(); CurrentIndex = -1; CurrentTrack = null;
        IsPlaying = IsLoading = IsBuffering = IsUserSeeking = IsFading = false;
        ErrorMessage = null; Position = Duration = TimeSpan.Zero; Progress = 0;
        RefreshControls();
    }

    private void OnAudioChanged(object? sender, AudioPlaybackSnapshot snapshot) => _ui.Post(_ =>
    {
        if (_disposed || snapshot.SourceId != _sourceId) return;
        if (snapshot.Error is { } error) { Fail(error); return; }
        IsPlaying = snapshot.IsPlaying; IsBuffering = snapshot.IsBuffering;
        Duration = snapshot.Duration; Position = snapshot.Position;
        _canSeek = snapshot.CanSeek; BufferingProgress = snapshot.BufferingProgress;
        if (!IsUserSeeking) Progress = Duration > TimeSpan.Zero ? Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds, 0, 1) : 0;
        if (!IsLoading && !_transitioning && !IsBuffering) _wantsPlay = IsPlaying;
        RefreshControls();
        if (snapshot.Ended && _handledEndId != _sourceId)
        {
            _handledEndId = _sourceId;
            if (Playlist.Count > 0 && CurrentIndex >= 0) _ = AdvanceAsync(true);
        }
    }, null);

    private void Fail(string message)
    {
        _transitionCts?.Cancel(); _ready = _canSeek = _wantsPlay = _transitioning = false;
        IsPlaying = IsLoading = IsBuffering = IsFading = IsUserSeeking = false;
        ErrorMessage = string.IsNullOrWhiteSpace(message) ? "播放失败，请点击播放重试。" : $"播放失败：{message}";
        RefreshControls();
    }
    private void RefreshControls()
    {
        foreach (var name in new[] { nameof(CanStartPlayback), nameof(CanSkip), nameof(CanSeek), nameof(HasTrack), nameof(ShowPause),
            nameof(IsBusy), nameof(PlayPauseLabel), nameof(TrackTitle), nameof(TrackSubtitle), nameof(TimeText), nameof(IsMuted) }) OnPropertyChanged(name);
        PlayCommand.NotifyCanExecuteChanged(); TogglePlayCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged(); NextCommand.NotifyCanExecuteChanged(); SeekCommand.NotifyCanExecuteChanged();
    }
    private static bool SameTrack(VideoModel a, VideoModel b) => a.Bvid == b.Bvid && a.Cid == b.Cid;
    partial void OnVolumeChanged(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1) { Volume = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0; return; }
        if (value > 0) _audibleVolume = value;
        _audio.Volume = value;
        OnPropertyChanged(nameof(IsMuted));
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _sourceCts?.Cancel(); _transitionCts?.Cancel();
        _sourceCts?.Dispose(); _transitionCts?.Dispose();
        _audio.Changed -= OnAudioChanged;
    }
}
