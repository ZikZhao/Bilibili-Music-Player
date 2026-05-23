using System;
using System.Collections.Generic;
using System.Globalization;
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
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

            var signed = new Dictionary<string, string>(parameters)
            {
                ["wts"] = timestamp,
            };

            var filtered = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in signed)
            {
                filtered[entry.Key] = FilterValue(entry.Value);
            }

            var queryBuilder = new StringBuilder();
            foreach (var entry in filtered)
            {
                if (queryBuilder.Length > 0)
                {
                    queryBuilder.Append('&');
                }

                queryBuilder.Append(Uri.EscapeDataString(entry.Key));
                queryBuilder.Append('=');
                queryBuilder.Append(Uri.EscapeDataString(entry.Value));
            }

            var hash = ComputeMd5(queryBuilder + mixinKey);
            filtered["w_rid"] = hash;

            return filtered;
        }

        private static string GetMixinKey(string source)
        {
            var buffer = new StringBuilder();
            for (var i = 0; i < 32; i++)
            {
                buffer.Append(source[MixinKeyEncTab[i]]);
            }

            return buffer.ToString();
        }

        private static string FilterValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var ch in value)
            {
                if (ch is '!' or '\'' or '(' or ')' or '*')
                {
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private static string ComputeMd5(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = MD5.HashData(bytes);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
