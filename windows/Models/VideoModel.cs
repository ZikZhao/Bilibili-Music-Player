using System;

namespace bilibili_music_player_windows.Models
{
    /// <summary>
    /// 统一视频/音频模型，用于播放器队列和 UI 绑定。
    /// 可通过 <see cref="VideoPreviewItem"/> 或 <see cref="VideoDetailInfo"/> 构造。
    /// </summary>
    public sealed class VideoModel
    {
        /// <summary>视频 BV 号。</summary>
        public string Bvid { get; init; } = string.Empty;

        /// <summary>视频 AV 号。</summary>
        public long Aid { get; init; }

        /// <summary>第一个分 P 的 cid。</summary>
        public long Cid { get; init; }

        /// <summary>视频标题。</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>UP 主名称（艺术家）。</summary>
        public string Artist { get; init; } = string.Empty;

        /// <summary>UP 主 MID。</summary>
        public long Mid { get; init; }

        /// <summary>封面图片 URL。</summary>
        public string Cover { get; init; } = string.Empty;

        /// <summary>格式化时长（mm:ss 或 h:mm:ss）。</summary>
        public string Duration { get; init; } = string.Empty;

        /// <summary>时长（秒）。</summary>
        public long DurationSeconds { get; init; }

        /// <summary>播放量（格式化后，如 "12.3万"）。</summary>
        public string Views { get; init; } = string.Empty;

        // ── Factory methods ──

        /// <summary>
        /// 从搜索结果的 <see cref="VideoPreviewItem"/> 构造。
        /// </summary>
        public static VideoModel FromPreviewItem(VideoPreviewItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return new VideoModel
            {
                Bvid = item.Bvid,
                Title = item.Title,
                Artist = item.Author,
                Cover = item.CoverUri?.ToString() ?? string.Empty,
                Duration = item.Duration,
                Views = item.Views,
                DurationSeconds = ParseDuration(item.Duration),
            };
        }

        /// <summary>
        /// 从 <see cref="VideoDetailInfo"/> 构造。
        /// </summary>
        public static VideoModel FromDetailInfo(VideoDetailInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);

            return new VideoModel
            {
                Bvid = info.Bvid,
                Aid = info.Aid,
                Cid = info.Cid,
                Title = info.Title,
                Artist = info.Owner?.Name ?? string.Empty,
                Mid = info.Owner?.Mid ?? 0,
                Cover = info.Cover,
                Duration = info.FormattedDuration,
                DurationSeconds = info.Duration,
                Views = FormatViewCount(info.Stat?.View ?? 0),
            };
        }

        /// <summary>
        /// 从 <see cref="VideoDetailInfo"/> + bvid/cid（当 info 缺少基础字段时）构造。
        /// </summary>
        public static VideoModel Create(string bvid, string title, string artist, string cover)
        {
            return new VideoModel
            {
                Bvid = bvid,
                Title = title,
                Artist = artist,
                Cover = cover,
            };
        }

        private static long ParseDuration(string duration)
        {
            if (string.IsNullOrWhiteSpace(duration))
            {
                return 0;
            }

            // Supports "mm:ss" and "h:mm:ss"
            var parts = duration.Split(':');
            if (parts.Length == 2 &&
                long.TryParse(parts[0], out var minutes) &&
                long.TryParse(parts[1], out var seconds))
            {
                return minutes * 60 + seconds;
            }

            if (parts.Length == 3 &&
                long.TryParse(parts[0], out var hours) &&
                long.TryParse(parts[1], out minutes) &&
                long.TryParse(parts[2], out seconds))
            {
                return hours * 3600 + minutes * 60 + seconds;
            }

            return 0;
        }

        private static string FormatViewCount(long count)
        {
            if (count >= 10_000)
            {
                return $"{(double)count / 10_000:F1}万";
            }

            return count.ToString("N0");
        }
    }
}
