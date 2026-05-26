using System.Globalization;
using System.Text.Json;

namespace bilibili_music_player_windows.Models
{
    /// <summary>
    /// 视频统计数据（播放量、弹幕数、收藏数等）。
    /// </summary>
    public sealed class VideoStat
    {
        public long View { get; init; }
        public long Danmaku { get; init; }
        public long Reply { get; init; }
        public long Favorite { get; init; }
        public long Coin { get; init; }
        public long Share { get; init; }
        public long Like { get; init; }

        /// <summary>
        /// 格式化播放量（如 "10.5万"）。
        /// </summary>
        public string FormattedView
        {
            get
            {
                if (View >= 100_000_000)
                {
                    return $"{(View / 100_000_000.0).ToString("F1", CultureInfo.InvariantCulture)}亿";
                }

                if (View >= 10_000)
                {
                    return $"{(View / 10_000.0).ToString("F1", CultureInfo.InvariantCulture)}万";
                }

                return View.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static VideoStat FromJsonElement(JsonElement element)
        {
            return new VideoStat
            {
                View = ReadInt64(element, "view") ?? 0,
                Danmaku = ReadInt64(element, "danmaku") ?? 0,
                Reply = ReadInt64(element, "reply") ?? 0,
                Favorite = ReadInt64(element, "favorite") ?? 0,
                Coin = ReadInt64(element, "coin") ?? 0,
                Share = ReadInt64(element, "share") ?? 0,
                Like = ReadInt64(element, "like") ?? 0,
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
