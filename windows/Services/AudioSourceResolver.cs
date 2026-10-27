using System;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Api;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Services;

public sealed class AudioSourceResolver(BilibiliClient client, CacheService cache) : IAudioSourceResolver
{
    public async Task<ResolvedAudioSource> ResolveAsync(VideoModel track, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(track.Bvid)) throw new InvalidOperationException("缺少音源标识，请重新收藏这首音乐。");
        var cached = await cache.GetAudioPathAsync(AudioSourceIdentity.CacheKey(track.Bvid, track.Cid));
        ct.ThrowIfCancellationRequested();
        if (cached is not null) return new(track, new Uri(cached));
        var detail = await client.FetchVideoInfoAsync(track.Bvid, ct);
        var cid = AudioSourceIdentity.ResolveCid(track.Cid, detail.Cid);
        var key = AudioSourceIdentity.CacheKey(track.Bvid, cid);
        cached = await cache.GetAudioPathAsync(key);
        if (cached is null && cid == detail.Cid) cached = await cache.GetAudioPathAsync(track.Bvid);
        ct.ThrowIfCancellationRequested();
        var resolved = new VideoModel
        {
            Bvid = track.Bvid, Aid = detail.Aid, Cid = cid,
            Title = string.IsNullOrEmpty(track.Title) ? detail.Title : track.Title,
            Artist = detail.Owner?.Name ?? track.Artist, Mid = detail.Owner?.Mid ?? track.Mid,
            Cover = string.IsNullOrEmpty(detail.Cover) ? track.Cover : detail.Cover,
            Duration = cid == detail.Cid ? detail.FormattedDuration : track.Duration,
            DurationSeconds = cid == detail.Cid ? detail.Duration : track.DurationSeconds, Views = track.Views,
        };
        if (cached is not null) return new(resolved, new Uri(cached));
        var play = await client.FetchPlayUrlAsync(track.Bvid, cid, audioOnly: true, ct);
        ct.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(play.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("未获取到有效的音频地址，请重试。");
        return new(resolved, uri, key);
    }

    public async Task CacheAsync(ResolvedAudioSource source, CancellationToken ct)
    {
        if (source.CacheKey is not null && !source.Uri.IsFile)
            await cache.DownloadAudioAsync(source.Uri.AbsoluteUri, source.CacheKey, ct);
    }
}
