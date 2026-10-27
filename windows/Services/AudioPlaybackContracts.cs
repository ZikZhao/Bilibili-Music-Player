using System;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Services;

public sealed record ResolvedAudioSource(VideoModel Track, Uri Uri, string? CacheKey = null);
public sealed record AudioPlaybackSnapshot(long SourceId, TimeSpan Position, TimeSpan Duration,
    bool IsPlaying, bool IsBuffering, bool CanSeek,
    bool Ended = false, string? Error = null);

public interface IAudioSourceResolver
{
    Task<ResolvedAudioSource> ResolveAsync(VideoModel track, CancellationToken ct);
    Task CacheAsync(ResolvedAudioSource source, CancellationToken ct) => Task.CompletedTask;
}

public interface IAudioPlaybackEngine
{
    event EventHandler<AudioPlaybackSnapshot>? Changed;
    double Volume { get; set; }
    Task OpenAsync(Uri uri, long sourceId, CancellationToken ct);
    void Resume();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
    Task FadeToAsync(double volume, TimeSpan duration, CancellationToken ct);
}
