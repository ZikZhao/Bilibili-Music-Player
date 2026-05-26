using System;
using System.Globalization;
using System.Text.Json;

namespace bilibili_music_player_windows.Models
{
    /// <summary>
    /// 视频详情信息，从 /x/web-interface/view API 返回。
    /// </summary>
    public sealed class VideoDetailInfo
    {
        /// <summary>视频 BV 号。</summary>
        public string Bvid { get; init; } = string.Empty;

        /// <summary>视频 AV 号。</summary>
        public long Aid { get; init; }

        /// <summary>第一个分 P 的 cid。</summary>
        public long Cid { get; init; }

        /// <summary>视频标题。</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>视频简介。</summary>
        public string Desc { get; init; } = string.Empty;

        /// <summary>视频封面 URL。</summary>
        public string Cover { get; init; } = string.Empty;

        /// <summary>UP 主信息。</summary>
        public OwnerInfo Owner { get; init; } = new();

        /// <summary>视频数据统计。</summary>
        public VideoStat Stat { get; init; } = new();

        /// <summary>发布时间戳（Unix 秒）。</summary>
        public long Pubdate { get; init; }

        /// <summary>视频时长（秒）。</summary>
        public long Duration { get; init; }

        /// <summary>分 P 数量。</summary>
        public long Videos { get; init; } = 1;

        /// <summary>格式化发布时间。</summary>
        public string FormattedPubdate
        {
            get
            {
                if (Pubdate <= 0)
                {
                    return string.Empty;
                }

                var date = DateTimeOffset.FromUnixTimeSeconds(Pubdate).LocalDateTime;
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>格式化时长。</summary>
        public string FormattedDuration
        {
            get
            {
                var hours = Duration / 3600;
                var minutes = (Duration % 3600) / 60;
                var seconds = Duration % 60;

                if (hours > 0)
                {
                    return $"{hours}:{minutes:D2}:{seconds:D2}";
                }

                return $"{minutes:D2}:{seconds:D2}";
            }
        }

        public static VideoDetailInfo FromJsonElement(JsonElement dataElement)
        {
            var cover = GetNullableString(dataElement, "pic") ?? string.Empty;
            if (cover.StartsWith("//", StringComparison.Ordinal))
            {
                cover = $"https:{cover}";
            }

            OwnerInfo owner;
            if (dataElement.TryGetProperty("owner", out var ownerElement))
            {
                owner = OwnerInfo.FromJsonElement(ownerElement);
            }
            else
            {
                owner = new OwnerInfo();
            }

            VideoStat stat;
            if (dataElement.TryGetProperty("stat", out var statElement))
            {
                stat = VideoStat.FromJsonElement(statElement);
            }
            else
            {
                stat = new VideoStat();
            }

            return new VideoDetailInfo
            {
                Bvid = GetNullableString(dataElement, "bvid") ?? string.Empty,
                Aid = ReadInt64(dataElement, "aid") ?? 0,
                Cid = ReadInt64(dataElement, "cid") ?? 0,
                Title = GetNullableString(dataElement, "title") ?? string.Empty,
                Desc = GetNullableString(dataElement, "desc") ?? string.Empty,
                Cover = cover,
                Owner = owner,
                Stat = stat,
                Pubdate = ReadInt64(dataElement, "pubdate") ?? 0,
                Duration = ReadInt64(dataElement, "duration") ?? 0,
                Videos = ReadInt64(dataElement, "videos") ?? 1,
            };
        }

        private static string? GetNullableString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => value.GetRawText(),
            };
        }

        private static long? ReadInt64(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String &&
                long.TryParse(value.GetString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
