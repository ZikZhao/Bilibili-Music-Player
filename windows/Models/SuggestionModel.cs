using System.Net;
using System.Text.RegularExpressions;

namespace bilibili_music_player_windows.Models
{
    /// <summary>
    /// 搜索建议模型，对应 s.search.bilibili.com/main/suggest API 返回的 tag 项。
    /// </summary>
    public sealed class SuggestionModel
    {
        /// <summary>
        /// 建议关键词（用于搜索）。
        /// </summary>
        public string Value { get; init; } = string.Empty;

        /// <summary>
        /// 显示名称（已去除 HTML 标签）。
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// 从 API JSON 创建实例。
        /// </summary>
        public static SuggestionModel? FromJsonElement(System.Text.Json.JsonElement element)
        {
            var rawValue = ReadNullableString(element, "value");
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            var rawName = ReadNullableString(element, "name") ?? rawValue;

            // 去除 HTML 标签
            var cleanName = StripHtmlTags(rawName);

            return new SuggestionModel
            {
                Value = rawValue,
                Name = string.IsNullOrWhiteSpace(cleanName) ? rawValue : cleanName,
            };
        }

        private static string? ReadNullableString(System.Text.Json.JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => value.GetString(),
                System.Text.Json.JsonValueKind.Number => value.GetRawText(),
                _ => value.GetRawText(),
            };
        }

        private static string StripHtmlTags(string text)
        {
            return Regex.Replace(WebUtility.HtmlDecode(text), "<[^>]+>", string.Empty);
        }

        public override string ToString() => $"SuggestionModel(Value: {Value})";

        public override bool Equals(object? obj) =>
            obj is SuggestionModel other && string.Equals(other.Value, Value, System.StringComparison.Ordinal);

        public override int GetHashCode() => Value.GetHashCode(System.StringComparison.Ordinal);
    }
}
