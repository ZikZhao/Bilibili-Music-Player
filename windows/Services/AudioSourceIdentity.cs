using System;

namespace bilibili_music_player_windows.Services;

public static class AudioSourceIdentity
{
    public static long ResolveCid(long requestedCid, long firstCid) => requestedCid > 0 ? requestedCid :
        firstCid > 0 ? firstCid : throw new InvalidOperationException("音源缺少有效的分 P 标识。");

    public static string CacheKey(string bvid, long cid)
    {
        if (string.IsNullOrWhiteSpace(bvid) || cid < 0) throw new ArgumentException("无效的音源标识。");
        return cid > 0 ? $"{bvid}__cid_{cid}" : bvid;
    }
}
