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
    private readonly ObservableCollection<PlaybackQueueEntry> _queue = new();
    private PlaybackQueueEntry? _currentEntry;
#if DEBUG
    private IReadOnlyDictionary<string, Uri>? _inspectionSources;
    internal long InspectionSourceId => _sourceId;
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
    [ObservableProperty] private double _volume = .8;
    [ObservableProperty] private int _queueCount;
    [ObservableProperty] private PlayMode _playMode = PlayMode.Loop;
    [ObservableProperty] private bool _isFading;
    [ObservableProperty] private bool _enableFade = true;
    [ObservableProperty] private bool _isUserSeeking;
    public ReadOnlyObservableCollection<PlaybackQueueEntry> Playlist { get; }
    public int CurrentIndex => _currentEntry is null ? -1 : _queue.IndexOf(_currentEntry);
    public bool HasQueue => Playlist.Count > 0;
    public bool CanShuffle => Playlist.Count - (_currentEntry is null ? 0 : 1) > 1;
    public bool IsSingleRepeat => PlayMode == PlayMode.Single;
    public string PlayModeLabel => IsSingleRepeat ? "单曲循环" : "列表循环";
    public int PlayModeIndex { get => IsSingleRepeat ? 1 : 0; set => PlayMode = value == 1 ? PlayMode.Single : PlayMode.Loop; }
    public string QueueSummary => $"共 {QueueCount} 首";

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
        Playlist = new(_queue);
        _queue.CollectionChanged += (_, _) => { QueueCount = Playlist.Count; RefreshQueueEntries(); RefreshControls(); };
    }

    [RelayCommand(CanExecute = nameof(CanStartPlayback), AllowConcurrentExecutions = true)]
    private Task TogglePlayAsync() => (IsLoading || _transitioning ? _wantsPlay : ShowPause) ? PauseAsync() : PlayAsync();

    [RelayCommand(CanExecute = nameof(CanStartPlayback), AllowConcurrentExecutions = true)]
    private async Task PlayAsync()
    {
        if (CurrentTrack is null) { if (Playlist.Count > 0) await LoadQueueAsync(true); return; }
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
        var index = _queue.ToList().FindIndex(v => SameTrack(v.Track, track));
        if (index < 0) _queue.Insert(0, new(track));
        else RotateTo(index);
        return LoadQueueAsync(true);
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
            CurrentTrack = resolved.Track;
            if (_currentEntry is not null) _currentEntry.Track = resolved.Track;
            RefreshControls();
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
        var autoPlay = automatic || CurrentTrack is null || ShowPause;
        if (_currentEntry is not null && !(automatic && IsSingleRepeat) && Playlist.Count > 1)
            _queue.Move(0, Playlist.Count - 1);
        return LoadQueueAsync(autoPlay);
    }

    [RelayCommand(CanExecute = nameof(CanSkip), AllowConcurrentExecutions = true)]
    private Task PreviousAsync()
    {
        var autoPlay = CurrentTrack is null || ShowPause;
        if (Playlist.Count > 1) _queue.Move(Playlist.Count - 1, 0);
        return LoadQueueAsync(autoPlay);
    }

    [RelayCommand]
    private void CyclePlayMode() => PlayMode = IsSingleRepeat ? PlayMode.Loop : PlayMode.Single;

    [RelayCommand(CanExecute = nameof(CanShuffle))]
    private void ShuffleUpcoming()
    {
        // Select each upcoming position uniformly from the remaining entries.
        // Move preserves the row identities and never replaces the active source.
        var start = _currentEntry is null ? 0 : 1;
        for (var i = start; i < _queue.Count - 1; i++)
            _queue.Move(Random.Shared.Next(i, _queue.Count), i);
    }

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
        _currentEntry = null;
        _queue.Clear(); foreach (var track in snapshot) _queue.Add(new(track));
        RotateTo(Math.Clamp(startIndex, 0, Playlist.Count - 1));
        await LoadQueueAsync(true);
    }
    public void AddToPlaylist(VideoModel track) => _queue.Add(new(track));
    public void Enqueue(IReadOnlyList<VideoModel> tracks, bool next)
    {
        var index = next ? (_currentEntry is null ? 0 : 1) : Playlist.Count;
        foreach (var track in tracks) _queue.Insert(index++, new(track));
    }
    public void RemoveFromPlaylist(int index)
        => _ = RemoveQueueEntryAsync(index >= 0 && index < _queue.Count ? _queue[index] : null);

    public async Task RemoveQueueEntryAsync(PlaybackQueueEntry? entry)
    {
        if (entry is null || !_queue.Contains(entry)) return;
        var current = ReferenceEquals(entry, _currentEntry); var wanted = _wantsPlay;
        if (current) _currentEntry = null;
        _queue.Remove(entry);
        if (Playlist.Count == 0) { ClearPlaylist(); return; }
        if (current) await LoadQueueAsync(wanted);
    }

    public Task PlayQueueEntryAsync(PlaybackQueueEntry entry)
    {
        var index = _queue.IndexOf(entry);
        if (index < 0) return Task.CompletedTask;
        if (ReferenceEquals(entry, _currentEntry)) return PlayAsync();
        RotateTo(index);
        return LoadQueueAsync(true);
    }
    private void RotateTo(int index)
    { for (var i = 0; i < index; i++) _queue.Move(0, _queue.Count - 1); }
    private Task LoadQueueAsync(bool autoPlay)
    {
        if (_queue.Count == 0) return Task.CompletedTask;
        _currentEntry = _queue[0]; RefreshQueueEntries(); return LoadAsync(_currentEntry.Track, autoPlay);
    }

    [RelayCommand(CanExecute = nameof(HasQueue))]
    public void ClearPlaylist()
    {
        ++_sourceId; _sourceCts?.Cancel(); _transitionCts?.Cancel();
        _audio.Stop(); _ready = _canSeek = _wantsPlay = _transitioning = false;
        _currentEntry = null; _queue.Clear(); CurrentTrack = null;
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
        _canSeek = snapshot.CanSeek;
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
    private void RefreshQueueEntries()
    {
        var hasCurrent = _currentEntry is not null;
        for (var i = 0; i < _queue.Count; i++)
        {
            var current = ReferenceEquals(_queue[i], _currentEntry);
            _queue[i].Update(hasCurrent ? i : i + 1, current, current && IsPlaying, IsSingleRepeat && !current);
        }
    }
    private void RefreshControls()
    {
        if (_currentEntry is not null) _currentEntry.IsPlaying = IsPlaying;
        foreach (var name in new[] { nameof(CanStartPlayback), nameof(CanSkip), nameof(CanSeek), nameof(HasTrack), nameof(ShowPause),
            nameof(IsBusy), nameof(PlayPauseLabel), nameof(TrackTitle), nameof(TrackSubtitle), nameof(TimeText), nameof(IsMuted),
            nameof(CurrentIndex), nameof(HasQueue), nameof(CanShuffle), nameof(IsSingleRepeat), nameof(PlayModeLabel), nameof(PlayModeIndex), nameof(QueueSummary) }) OnPropertyChanged(name);
        PlayCommand.NotifyCanExecuteChanged(); TogglePlayCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged(); NextCommand.NotifyCanExecuteChanged(); SeekCommand.NotifyCanExecuteChanged();
        ShuffleUpcomingCommand.NotifyCanExecuteChanged(); ClearPlaylistCommand.NotifyCanExecuteChanged();
    }
    partial void OnPlayModeChanged(PlayMode value) { RefreshQueueEntries(); RefreshControls(); }
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
