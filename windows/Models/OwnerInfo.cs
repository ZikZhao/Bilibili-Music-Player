using System;
using System.Globalization;
using System.Text.Json;

namespace bilibili_music_player_windows.Models
{
    /// <summary>
    /// UP 主信息。
    /// </summary>
    public sealed class OwnerInfo
    {
        /// <summary>
        /// UP 主 MID。
        /// </summary>
        public long Mid { get; init; }

        /// <summary>
        /// UP 主昵称。
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// UP 主头像 URL。
        /// </summary>
        public string Face { get; init; } = string.Empty;

        public static OwnerInfo FromJsonElement(JsonElement element)
        {
            var face = GetNullableString(element, "face") ?? string.Empty;
            if (face.StartsWith("//", StringComparison.Ordinal))
            {
                face = $"https:{face}";
            }

            return new OwnerInfo
            {
                Mid = ReadInt64(element, "mid") ?? 0,
                Name = GetNullableString(element, "name") ?? string.Empty,
                Face = face,
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
                long.TryParse(value.GetString(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
