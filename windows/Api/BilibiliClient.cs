using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Api
{
    public sealed class BilibiliClient : IDisposable
    {
        private const string BaseUrl = "https://api.bilibili.com";
        private const string SuggestBaseUrl = "https://s.search.bilibili.com";
        private const string WebBaseUrl = "https://www.bilibili.com/";
        private static readonly Uri WebBaseUri = new(WebBaseUrl);

        private readonly HttpClient _client;
        private readonly WbiSigner _wbiSigner = new();
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _initialized;

        /// <summary>
        /// Applies the standard Bilibili HTTP headers to an <see cref="HttpClient"/>.
        /// </summary>
        public static void ConfigureDefaultHeaders(HttpClient client)
        {
            ArgumentNullException.ThrowIfNull(client);

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.bilibili.com");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        }

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

            ConfigureDefaultHeaders(_client);
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

            // A1: Risk detection — wrap in try-catch for 412 + retry
            HttpResponseMessage response;
            try
            {
                response = await _client.GetAsync(requestUri);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // 412: Retry once after a brief delay
                await Task.Delay(1000);
                response = await _client.GetAsync(requestUri);
            }

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

            // A1: Risk detection — check for v_voucher in response
            if (dataElement.TryGetProperty("v_voucher", out _))
            {
                throw new BilibiliApiException("请求被风控系统拦截，请稍后再试。如持续出现此问题，请尝试重启应用。");
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

        /// <summary>
        /// 获取搜索建议（A2）。
        /// </summary>
        /// <param name="keyword">搜索关键词。</param>
        /// <returns>建议列表，最多 10 个。</returns>
        public async Task<List<SuggestionModel>> FetchSuggestionsAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return [];
            }

            try
            {
                var requestUri = BuildUri($"{SuggestBaseUrl}/main/suggest", new Dictionary<string, string>
                {
                    ["term"] = keyword,
                    ["main_ver"] = "v1",
                    ["func"] = "suggest",
                    ["suggest_type"] = "accurate",
                    ["sub_type"] = "tag",
                    ["tag_num"] = "10",
                });

                using var response = await _client.GetAsync(requestUri);
                response.EnsureSuccessStatusCode();

                var rawBody = await response.Content.ReadAsStringAsync();

                // 响应可能是纯 JSON 字符串（带转义），先尝试解析外层
                using var document = JsonDocument.Parse(rawBody);
                var root = document.RootElement;

                // 如果 code != 0，静默返回空
                var code = ReadInt32(root, "code");
                if (code != 0)
                {
                    return [];
                }

                if (!root.TryGetProperty("result", out var resultElement) ||
                    !resultElement.TryGetProperty("tag", out var tagElement) ||
                    tagElement.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                var suggestions = new List<SuggestionModel>(tagElement.GetArrayLength());
                foreach (var item in tagElement.EnumerateArray())
                {
                    var model = SuggestionModel.FromJsonElement(item);
                    if (model is not null)
                    {
                        suggestions.Add(model);
                    }
                }

                return suggestions;
            }
            catch (Exception)
            {
                // 建议接口失败不抛异常，返回空列表
                return [];
            }
        }

        public async Task<Uri> FetchPreviewUriAsync(string bvid)
        {
            await EnsureInitializedAsync();

            if (string.IsNullOrWhiteSpace(bvid))
            {
                throw new BilibiliApiException("缺少视频标识。");
            }

            var videoInfo = await FetchVideoInfoAsync(bvid);
            var playUrlInfo = await FetchPlayUrlAsync(bvid, videoInfo.Cid, audioOnly: false);

            if (!Uri.TryCreate(playUrlInfo.Url, UriKind.Absolute, out var result))
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

        /// <summary>
        /// 获取视频详情信息（A4），返回完整的 <see cref="VideoDetailInfo"/>（包含 cid、owner、stat 等）。
        /// </summary>
        public async Task<VideoDetailInfo> FetchVideoInfoAsync(string bvid)
        {
            await EnsureInitializedAsync();

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

            var info = VideoDetailInfo.FromJsonElement(dataElement);

            if (info.Cid <= 0)
            {
                // Fallback: try pages array
                if (dataElement.TryGetProperty("pages", out var pagesElement) &&
                    pagesElement.ValueKind == JsonValueKind.Array &&
                    pagesElement.GetArrayLength() > 0)
                {
                    var firstPage = pagesElement[0];
                    var pageCid = ReadInt64(firstPage, "cid");
                    if (pageCid.HasValue && pageCid.Value > 0)
                    {
                        info = new VideoDetailInfo
                        {
                            Bvid = info.Bvid,
                            Aid = info.Aid,
                            Cid = pageCid.Value,
                            Title = info.Title,
                            Desc = info.Desc,
                            Cover = info.Cover,
                            Owner = info.Owner,
                            Stat = info.Stat,
                            Pubdate = info.Pubdate,
                            Duration = info.Duration,
                            Videos = info.Videos,
                        };
                        return info;
                    }
                }

                throw new BilibiliApiException("获取视频信息失败：未找到有效的 cid。");
            }

            return info;
        }

        /// <summary>
        /// 获取播放地址（A5/A6）。
        /// </summary>
        /// <param name="bvid">视频 BV 号。</param>
        /// <param name="cid">分 P 的 cid。</param>
        /// <param name="audioOnly">
        /// true: 使用 DASH 格式获取纯音频流（用于音乐播放器）。
        /// false: 使用 MP4 格式获取完整视频（用于视频播放器）。
        /// </param>
        public async Task<PlayUrlInfo> FetchPlayUrlAsync(string bvid, long cid, bool audioOnly = true)
        {
            await EnsureInitializedAsync();

            if (!audioOnly)
            {
                return await FetchPlayUrlMp4Async(bvid, cid);
            }

            // A5: DASH 音频模式
            return await FetchPlayUrlDashAsync(bvid, cid);
        }

        /// <summary>
        /// DASH 音频模式（fnval=16），支持 A6 MP4 fallback。
        /// </summary>
        private async Task<PlayUrlInfo> FetchPlayUrlDashAsync(string bvid, long cid)
        {
            var signedParams = _wbiSigner.Sign(new Dictionary<string, string>
            {
                ["bvid"] = bvid,
                ["cid"] = cid.ToString(CultureInfo.InvariantCulture),
                ["qn"] = "64",
                ["fnval"] = "16",  // A5: DASH 格式
                ["fnver"] = "0",
                ["fourk"] = "1",
            });

            var requestUri = BuildUri($"{BaseUrl}/x/player/wbi/playurl", signedParams);

            HttpResponseMessage response;
            try
            {
                response = await _client.GetAsync(requestUri);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // 412: Retry once
                await Task.Delay(1000);
                response = await _client.GetAsync(requestUri);
            }

            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;

            var code = ReadInt32(root, "code");
            if (code != 0)
            {
                var message = ReadString(root, "message") ?? "获取播放地址失败。";
                throw new BilibiliApiException(message);
            }

            if (!root.TryGetProperty("data", out var dataElement))
            {
                throw new BilibiliApiException("获取播放地址失败：响应缺少数据。");
            }

            // A5: 从 DASH 格式获取音频流
            if (dataElement.TryGetProperty("dash", out var dashElement))
            {
                if (dashElement.TryGetProperty("audio", out var audioArray) &&
                    audioArray.ValueKind == JsonValueKind.Array &&
                    audioArray.GetArrayLength() > 0)
                {
                    // 按 bandwidth 排序，取最高质量
                    var bestAudio = audioArray.EnumerateArray()
                        .OrderByDescending(a =>
                        {
                            var bw = ReadInt32(a, "bandwidth");
                            return bw ?? 0;
                        })
                        .First();

                    var url = ReadString(bestAudio, "baseUrl")
                              ?? ReadString(bestAudio, "base_url")
                              ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        var bandwidth = ReadInt32(bestAudio, "bandwidth") ?? 0;
                        var codecId = ReadInt32(bestAudio, "codecid") ?? 0;
                        var timeLength = ReadInt64(dataElement, "timelength") ?? 0;

                        return new PlayUrlInfo
                        {
                            Url = url,
                            Quality = bandwidth,
                            Format = "m4s",
                            Size = 0,
                            Length = timeLength,
                            IsAudioOnly = true,
                        };
                    }
                }
            }

            // A6: DASH 无可用音频 → MP4 fallback
            return await FetchPlayUrlMp4Async(bvid, cid);
        }

        /// <summary>
        /// MP4 格式播放地址（fnval=1），传统直链。
        /// </summary>
        private async Task<PlayUrlInfo> FetchPlayUrlMp4Async(string bvid, long cid)
        {
            var signedParams = _wbiSigner.Sign(new Dictionary<string, string>
            {
                ["bvid"] = bvid,
                ["cid"] = cid.ToString(CultureInfo.InvariantCulture),
                ["qn"] = "64",
                ["fnval"] = "1",  // MP4 格式
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

            var size = ReadInt64(firstUrl, "size") ?? 0;
            var length = ReadInt64(firstUrl, "length") ?? 0;
            var quality = ReadInt32(dataElement, "quality") ?? 64;

            return new PlayUrlInfo
            {
                Url = url,
                Quality = quality,
                Format = "mp4",
                Size = size,
                Length = length,
                IsAudioOnly = false,
            };
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

        public void Dispose()
        {
            _initLock.Dispose();
            _client.Dispose();
        }
    }
}
