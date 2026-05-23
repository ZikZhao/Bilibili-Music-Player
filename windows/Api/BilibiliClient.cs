using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Api
{
    public sealed class BilibiliClient
    {
        private const string BaseUrl = "https://api.bilibili.com";
        private const string WebBaseUrl = "https://www.bilibili.com/";
        private static readonly Uri WebBaseUri = new(WebBaseUrl);

        private readonly HttpClient _client;
        private readonly WbiSigner _wbiSigner = new();
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _initialized;

        public BilibiliClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                UseCookies = true,
                CookieContainer = new CookieContainer(),
            };

            _client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15),
            };

            _client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.bilibili.com");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        }

        public async Task<SearchResult> SearchVideosAsync(string keyword, int page = 1, int pageSize = 20)
        {
            await EnsureInitializedAsync();

            if (string.IsNullOrWhiteSpace(keyword))
            {
                return new SearchResult(Array.Empty<VideoPreviewItem>(), page, pageSize, 0, 0);
            }

            var signedParams = _wbiSigner.Sign(new Dictionary<string, string>
            {
                ["search_type"] = "video",
                ["keyword"] = keyword,
                ["page"] = page.ToString(CultureInfo.InvariantCulture),
                ["page_size"] = pageSize.ToString(CultureInfo.InvariantCulture),
            });

            var requestUri = BuildUri($"{BaseUrl}/x/web-interface/wbi/search/type", signedParams);

            using var response = await _client.GetAsync(requestUri);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;

            var code = ReadInt32(root, "code");
            if (code != 0)
            {
                var message = ReadString(root, "message") ?? "搜索失败，请稍后再试。";
                throw new BilibiliApiException(message);
            }

            if (!root.TryGetProperty("data", out var dataElement))
            {
                return new SearchResult(Array.Empty<VideoPreviewItem>(), page, pageSize, 0, 0);
            }

            if (!dataElement.TryGetProperty("result", out var resultElement) ||
                resultElement.ValueKind != JsonValueKind.Array)
            {
                return new SearchResult(Array.Empty<VideoPreviewItem>(), page, pageSize, 0, 0);
            }

            var videos = new List<VideoPreviewItem>();
            foreach (var item in resultElement.EnumerateArray())
            {
                var bvid = ReadString(item, "bvid");
                if (string.IsNullOrWhiteSpace(bvid))
                {
                    continue;
                }

                var title = CleanTitle(ReadString(item, "title"));
                var author = ReadString(item, "author") ?? "未知作者";
                var duration = ReadString(item, "duration") ?? "00:00";
                var views = ReadString(item, "play") ?? "0";
                var coverUri = ResolveCoverUri(ReadString(item, "pic"));

                videos.Add(new VideoPreviewItem
                {
                    Bvid = bvid,
                    Title = title,
                    Author = author,
                    Duration = duration,
                    Views = views,
                    CoverUri = coverUri,
                });
            }

            var resultPage = ReadInt32(dataElement, "page") ?? page;
            var resultPageSize = ReadInt32(dataElement, "pagesize") ?? pageSize;
            var numResults = ReadInt32(dataElement, "numResults") ?? videos.Count;
            var numPages = ReadInt32(dataElement, "numPages") ?? 1;

            return new SearchResult(videos, resultPage, resultPageSize, numResults, numPages);
        }

        public async Task<Uri> FetchPreviewUriAsync(string bvid)
        {
            await EnsureInitializedAsync();

            if (string.IsNullOrWhiteSpace(bvid))
            {
                throw new BilibiliApiException("缺少视频标识。");
            }

            var cid = await FetchVideoCidAsync(bvid);
            var playUrl = await FetchPlayUrlAsync(bvid, cid);

            if (!Uri.TryCreate(playUrl, UriKind.Absolute, out var result))
            {
                throw new BilibiliApiException("获取到无效的播放地址。");
            }

            return result;
        }

        private async Task EnsureInitializedAsync()
        {
            if (_initialized)
            {
                return;
            }

            await _initLock.WaitAsync();
            try
            {
                if (_initialized)
                {
                    return;
                }

                await InitializeAsync();
                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task InitializeAsync()
        {
            using var webResponse = await _client.GetAsync(WebBaseUri);
            webResponse.EnsureSuccessStatusCode();

            using var response = await _client.GetAsync($"{BaseUrl}/x/web-interface/nav");
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;

            var code = ReadInt32(root, "code");
            if (code != 0 && code != -101)
            {
                var message = ReadString(root, "message") ?? "初始化失败。";
                throw new BilibiliApiException(message);
            }

            if (!root.TryGetProperty("data", out var dataElement) ||
                !dataElement.TryGetProperty("wbi_img", out var wbiElement))
            {
                throw new BilibiliApiException("初始化失败：未获取到 WBI 密钥。");
            }

            var imgUrl = ReadString(wbiElement, "img_url");
            var subUrl = ReadString(wbiElement, "sub_url");

            if (string.IsNullOrWhiteSpace(imgUrl) || string.IsNullOrWhiteSpace(subUrl))
            {
                throw new BilibiliApiException("初始化失败：WBI 密钥为空。");
            }

            var imgKey = ExtractWbiKey(imgUrl);
            var subKey = ExtractWbiKey(subUrl);

            if (string.IsNullOrWhiteSpace(imgKey) || string.IsNullOrWhiteSpace(subKey))
            {
                throw new BilibiliApiException("初始化失败：WBI 密钥格式不正确。");
            }

            _wbiSigner.SetKeys(imgKey, subKey);
        }

        private async Task<long> FetchVideoCidAsync(string bvid)
        {
            var requestUri = $"{BaseUrl}/x/web-interface/view?bvid={Uri.EscapeDataString(bvid)}";

            using var response = await _client.GetAsync(requestUri);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;

            var code = ReadInt32(root, "code");
            if (code != 0)
            {
                var message = ReadString(root, "message") ?? "获取视频信息失败。";
                throw new BilibiliApiException(message);
            }

            if (!root.TryGetProperty("data", out var dataElement))
            {
                throw new BilibiliApiException("获取视频信息失败：响应缺少数据。");
            }

            var cid = ReadInt64(dataElement, "cid");
            if (cid.HasValue && cid.Value > 0)
            {
                return cid.Value;
            }

            if (dataElement.TryGetProperty("pages", out var pagesElement) &&
                pagesElement.ValueKind == JsonValueKind.Array &&
                pagesElement.GetArrayLength() > 0)
            {
                var firstPage = pagesElement[0];
                var pageCid = ReadInt64(firstPage, "cid");
                if (pageCid.HasValue && pageCid.Value > 0)
                {
                    return pageCid.Value;
                }
            }

            throw new BilibiliApiException("获取视频信息失败：未找到有效的 cid。");
        }

        private async Task<string> FetchPlayUrlAsync(string bvid, long cid)
        {
            var signedParams = _wbiSigner.Sign(new Dictionary<string, string>
            {
                ["bvid"] = bvid,
                ["cid"] = cid.ToString(CultureInfo.InvariantCulture),
                ["qn"] = "64",
                ["fnval"] = "1",
                ["fnver"] = "0",
                ["fourk"] = "1",
            });

            var requestUri = BuildUri($"{BaseUrl}/x/player/wbi/playurl", signedParams);

            using var response = await _client.GetAsync(requestUri);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;

            var code = ReadInt32(root, "code");
            if (code != 0)
            {
                var message = ReadString(root, "message") ?? "获取播放地址失败。";
                throw new BilibiliApiException(message);
            }

            if (!root.TryGetProperty("data", out var dataElement) ||
                !dataElement.TryGetProperty("durl", out var durlElement) ||
                durlElement.ValueKind != JsonValueKind.Array ||
                durlElement.GetArrayLength() == 0)
            {
                throw new BilibiliApiException("获取播放地址失败：没有可用的视频流。");
            }

            var firstUrl = durlElement[0];
            var url = ReadString(firstUrl, "url");

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new BilibiliApiException("获取播放地址失败：播放地址为空。");
            }

            return url;
        }

        private static Uri BuildUri(string baseUrl, IReadOnlyDictionary<string, string> query)
        {
            var parts = new List<string>(query.Count);
            foreach (var entry in query)
            {
                parts.Add($"{Uri.EscapeDataString(entry.Key)}={Uri.EscapeDataString(entry.Value)}");
            }

            return new Uri($"{baseUrl}?{string.Join("&", parts)}");
        }

        private static string ExtractWbiKey(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var filename = uri.Segments[^1];
            var dotIndex = filename.IndexOf('.');
            return dotIndex > 0 ? filename[..dotIndex] : filename;
        }

        private static string CleanTitle(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var decoded = WebUtility.HtmlDecode(raw);
            return Regex.Replace(decoded, "<[^>]+>", string.Empty);
        }

        private static Uri ResolveCoverUri(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new Uri("https://picsum.photos/seed/bili/480/270");
            }

            var normalized = raw.StartsWith("//", StringComparison.Ordinal)
                ? $"https:{raw}"
                : raw;

            if (Uri.TryCreate(normalized, UriKind.Absolute, out var result))
            {
                return result;
            }

            return new Uri("https://picsum.photos/seed/bili/480/270");
        }

        private static string? ReadString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.GetRawText(),
            };
        }

        private static int? ReadInt32(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String &&
                int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return null;
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
                long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
