using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace bilibili_music_player_windows.Api
{
    public sealed class WbiSigner
    {
        private static readonly int[] MixinKeyEncTab =
        [
            46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5,
            49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55,
            40, 61, 26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62,
            11, 36, 20, 34, 44, 52,
        ];

        // 小写 Hex 字符查找表，供 string.Create 直接使用
        private static ReadOnlySpan<byte> HexLower => "0123456789abcdef"u8;

        private string? _imgKey;
        private string? _subKey;

        public void SetKeys(string imgKey, string subKey)
        {
            _imgKey = imgKey;
            _subKey = subKey;
        }

        public IReadOnlyDictionary<string, string> Sign(IReadOnlyDictionary<string, string> parameters)
        {
            if (string.IsNullOrWhiteSpace(_imgKey) || string.IsNullOrWhiteSpace(_subKey))
            {
                throw new InvalidOperationException("WBI keys not initialized.");
            }

            var mixinKey = GetMixinKey(_imgKey + _subKey);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // ── 构建排序键值对：使用数组 + Array.Sort 替代 SortedDictionary（红黑树堆分配） ──
            var count = parameters.Count + 1; // +1 for wts
            var keys = new string[count];
            var values = new string[count];

            var i = 0;
            foreach (var entry in parameters)
            {
                keys[i] = entry.Key;
                values[i] = FilterValue(entry.Value);
                i++;
            }
            keys[i] = "wts";
            values[i] = timestamp;

            Array.Sort(keys, values, StringComparer.Ordinal);

            // ── 流式拼接查询字符串 ──
            var queryBuilder = new StringBuilder();
            for (var j = 0; j < keys.Length; j++)
            {
                if (j > 0)
                {
                    queryBuilder.Append('&');
                }

                queryBuilder.Append(Uri.EscapeDataString(keys[j]));
                queryBuilder.Append('=');
                queryBuilder.Append(Uri.EscapeDataString(values[j]));
            }

            var hash = ComputeMd5(queryBuilder + mixinKey);

            // 返回可变字典以附加 w_rid
            var result = new Dictionary<string, string>(count + 1);
            for (var j = 0; j < keys.Length; j++)
            {
                result[keys[j]] = values[j];
            }
            result["w_rid"] = hash;

            return result;
        }

        private static string GetMixinKey(string source)
        {
            Span<char> buffer = stackalloc char[32];
            for (var i = 0; i < 32; i++)
            {
                buffer[i] = source[MixinKeyEncTab[i]];
            }

            return new string(buffer);
        }

        private static string FilterValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var span = value.AsSpan();
            var illegalIndex = span.IndexOfAny(stackalloc char[] { '!', '\'', '(', ')', '*' });
            if (illegalIndex < 0)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            builder.Append(span[..illegalIndex]);
            for (var i = illegalIndex; i < span.Length; i++)
            {
                var ch = span[i];
                if (ch is not ('!' or '\'' or '(' or ')' or '*'))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static string ComputeMd5(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = MD5.HashData(bytes);

            // string.Create + 自定义小写 Hex 映射，避免 ToHexString→ToLowerInvariant 双重分配
            return string.Create(hash.Length * 2, hash, (span, data) =>
            {
                var hex = HexLower;
                for (var i = 0; i < data.Length; i++)
                {
                    var b = data[i];
                    span[i * 2] = (char)hex[b >> 4];
                    span[i * 2 + 1] = (char)hex[b & 0xF];
                }
            });
        }
    }
}
