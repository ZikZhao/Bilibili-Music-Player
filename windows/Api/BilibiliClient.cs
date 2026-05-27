using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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

        /// <summary>
        /// 使用 Lazy&lt;Task&gt; 保证初始化逻辑在极高并发下也绝对单例、无锁开销。
        /// ExecutionAndPublication 确保工厂函数最多执行一次，后续调用复用同一 Task。
        /// </summary>
        private readonly Lazy<Task> _initLazy;

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

            // Lazy<Task> 捕获实例引用，工厂闭包可访问 _client 和 _wbiSigner
            _initLazy = new Lazy<Task>(InitializeAsync, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public async Task<SearchResult> SearchVideosAsync(string keyword, int page = 1, int pageSize = 20, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct);

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
                response = await _client.GetAsync(requestUri, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // 412: Retry once after a brief delay
                await Task.Delay(1000, ct);
                response = await _client.GetAsync(requestUri, ct);
            }

            response.EnsureSuccessStatusCode();

            // ── Source-gen 零拷贝反序列化 ──
            using var searchStream = await response.Content.ReadAsStreamAsync(ct);
            var searchResponse = await JsonSerializer.DeserializeAsync(
                searchStream, BilibiliJsonContext.Default.SearchApiResponse, ct);

            if (searchResponse is null || searchResponse.Code != 0)
            {
                var msg = searchResponse?.Message ?? "搜索失败，请稍后再试。";
                throw new BilibiliApiException(msg);
            }

            var dataElement = searchResponse.Data;
            if (dataElement is null)
            {
                return new SearchResult(Array.Empty<VideoPreviewItem>(), page, pageSize, 0, 0);
            }

            // A1: Risk detection — check for v_voucher in response
            if (dataElement.V_voucher is not null)
            {
                throw new BilibiliApiException("请求被风控系统拦截，请稍后再试。如持续出现此问题，请尝试重启应用。");
            }

            if (dataElement.Result is not { } resultList)
            {
                return new SearchResult(Array.Empty<VideoPreviewItem>(), page, pageSize, 0, 0);
            }

            var videos = new List<VideoPreviewItem>(resultList.Count);
            foreach (var item in resultList)
            {
                if (string.IsNullOrWhiteSpace(item.Bvid))
                {
                    continue;
                }

                videos.Add(new VideoPreviewItem
                {
                    Bvid = item.Bvid,
                    Title = CleanTitle(item.Title),
                    Author = item.Author ?? "未知作者",
                    Duration = item.Duration ?? "00:00",
                    Views = ExtractPlayCount(item.Play),
                    CoverUri = ResolveCoverUri(item.Pic),
                });
            }

            return new SearchResult(
                videos,
                dataElement.Page ?? page,
                dataElement.Pagesize ?? pageSize,
                dataElement.NumResults ?? videos.Count,
                dataElement.NumPages ?? 1);
        }

        /// <summary>
        /// 获取搜索建议（A2）。
        /// </summary>
        /// <param name="keyword">搜索关键词。</param>
        /// <returns>建议列表，最多 10 个。</returns>
        public async Task<List<SuggestionModel>> FetchSuggestionsAsync(string keyword, CancellationToken ct = default)
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

                using var response = await _client.GetAsync(requestUri, ct);
                response.EnsureSuccessStatusCode();

                // ── Source-gen 零拷贝反序列化 ──
                using var suggestStream = await response.Content.ReadAsStreamAsync(ct);
                var suggestResponse = await JsonSerializer.DeserializeAsync(
                    suggestStream, BilibiliJsonContext.Default.SuggestApiResponse, ct);

                if (suggestResponse is null || suggestResponse.Code != 0)
                {
                    return [];
                }

                var tagList = suggestResponse.Result?.Tag;
                if (tagList is null || tagList.Count == 0)
                {
                    return [];
                }

                var suggestions = new List<SuggestionModel>(tagList.Count);
                foreach (var tag in tagList)
                {
                    if (string.IsNullOrWhiteSpace(tag.Value))
                    {
                        continue;
                    }

                    var name = CleanTitle(tag.Name);
                    suggestions.Add(new SuggestionModel
                    {
                        Value = tag.Value,
                        Name = string.IsNullOrWhiteSpace(name) ? tag.Value : name,
                    });
                }

                return suggestions;
            }
            catch (Exception)
            {
                // 建议接口失败不抛异常，返回空列表
                return [];
            }
        }

        public async Task<Uri> FetchPreviewUriAsync(string bvid, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct);

            if (string.IsNullOrWhiteSpace(bvid))
            {
                throw new BilibiliApiException("缺少视频标识。");
            }

            var videoInfo = await FetchVideoInfoAsync(bvid, ct);
            var playUrlInfo = await FetchPlayUrlAsync(bvid, videoInfo.Cid, audioOnly: false, ct);

            if (!Uri.TryCreate(playUrlInfo.Url, UriKind.Absolute, out var result))
            {
                throw new BilibiliApiException("获取到无效的播放地址。");
            }

            return result;
        }

        private Task EnsureInitializedAsync(CancellationToken ct = default)
        {
            // Lazy<Task> 保证 InitializeAsync 最多执行一次，
            // 后续调用直接返回同一 Task。WaitAsync 支持 CancellationToken。
            return _initLazy.Value.WaitAsync(ct);
        }

        /// <summary>
        /// 初始化：获取 WBI 密钥。由 Lazy&lt;Task&gt; 包装，确保仅执行一次。
        /// 捕获实例引用，可直接访问 _client 和 _wbiSigner。
        /// </summary>
        private async Task InitializeAsync()
        {
            using var webResponse = await _client.GetAsync(WebBaseUri);
            webResponse.EnsureSuccessStatusCode();

            using var response = await _client.GetAsync($"{BaseUrl}/x/web-interface/nav");
            response.EnsureSuccessStatusCode();

            // ── Source-gen 零拷贝反序列化 ──
            using var navStream = await response.Content.ReadAsStreamAsync();
            var navResponse = await JsonSerializer.DeserializeAsync(
                navStream, BilibiliJsonContext.Default.NavApiResponse);

            if (navResponse is null || (navResponse.Code != 0 && navResponse.Code != -101))
            {
                var msg = navResponse?.Message ?? "初始化失败。";
                throw new BilibiliApiException(msg);
            }

            var wbiImg = navResponse.Data?.WbiImg;
            if (wbiImg is null || string.IsNullOrWhiteSpace(wbiImg.ImgUrl) || string.IsNullOrWhiteSpace(wbiImg.SubUrl))
            {
                throw new BilibiliApiException("初始化失败：未获取到 WBI 密钥。");
            }

            var imgKey = ExtractWbiKey(wbiImg.ImgUrl);
            var subKey = ExtractWbiKey(wbiImg.SubUrl);

            if (string.IsNullOrWhiteSpace(imgKey) || string.IsNullOrWhiteSpace(subKey))
            {
                throw new BilibiliApiException("初始化失败：WBI 密钥格式不正确。");
            }

            _wbiSigner.SetKeys(imgKey, subKey);
        }

        /// <summary>
        /// 获取视频详情信息（A4），返回完整的 <see cref="VideoDetailInfo"/>（包含 cid、owner、stat 等）。
        /// </summary>
        public async Task<VideoDetailInfo> FetchVideoInfoAsync(string bvid, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct);

            var requestUri = $"{BaseUrl}/x/web-interface/view?bvid={Uri.EscapeDataString(bvid)}";

            using var response = await _client.GetAsync(requestUri, ct);
            response.EnsureSuccessStatusCode();

            // ── Source-gen 零拷贝反序列化 ──
            using var videoStream = await response.Content.ReadAsStreamAsync(ct);
            var videoResponse = await JsonSerializer.DeserializeAsync(
                videoStream, BilibiliJsonContext.Default.VideoDetailApiResponse, ct);

            if (videoResponse is null || videoResponse.Code != 0)
            {
                var msg = videoResponse?.Message ?? "获取视频信息失败。";
                throw new BilibiliApiException(msg);
            }

            var data = videoResponse.Data;
            if (data is null)
            {
                throw new BilibiliApiException("获取视频信息失败：响应缺少数据。");
            }

            var cid = data.Cid ?? 0;

            // Fallback: try pages array if cid is missing
            if (cid <= 0 && data.Pages is { Count: > 0 })
            {
                var firstPageCid = data.Pages[0].Cid ?? 0;
                if (firstPageCid > 0)
                {
                    cid = firstPageCid;
                }
            }

            if (cid <= 0)
            {
                throw new BilibiliApiException("获取视频信息失败：未找到有效的 cid。");
            }

            var cover = data.Cover ?? string.Empty;
            if (cover.StartsWith("//", StringComparison.Ordinal))
            {
                cover = $"https:{cover}";
            }

            var owner = data.Owner is not null
                ? new OwnerInfo
                {
                    Mid = data.Owner.Mid ?? 0,
                    Name = data.Owner.Name ?? string.Empty,
                    Face = data.Owner.Face ?? string.Empty,
                }
                : new OwnerInfo();

            var stat = data.Stat is not null
                ? new VideoStat
                {
                    View = data.Stat.View ?? 0,
                    Danmaku = data.Stat.Danmaku ?? 0,
                    Reply = data.Stat.Reply ?? 0,
                    Favorite = data.Stat.Favorite ?? 0,
                    Coin = data.Stat.Coin ?? 0,
                    Share = data.Stat.Share ?? 0,
                    Like = data.Stat.Like ?? 0,
                }
                : new VideoStat();

            return new VideoDetailInfo
            {
                Bvid = data.Bvid ?? string.Empty,
                Aid = data.Aid ?? 0,
                Cid = cid,
                Title = data.Title ?? string.Empty,
                Desc = data.Desc ?? string.Empty,
                Cover = cover,
                Owner = owner,
                Stat = stat,
                Pubdate = data.Pubdate ?? 0,
                Duration = data.Duration ?? 0,
                Videos = data.Videos ?? 1,
            };
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
        /// <param name="ct">取消令牌。</param>
        public async Task<PlayUrlInfo> FetchPlayUrlAsync(string bvid, long cid, bool audioOnly = true, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct);

            if (!audioOnly)
            {
                return await FetchPlayUrlMp4Async(bvid, cid, ct);
            }

            // A5: DASH 音频模式
            return await FetchPlayUrlDashAsync(bvid, cid, ct);
        }

        /// <summary>
        /// DASH 音频模式（fnval=16），支持 A6 MP4 fallback。
        /// </summary>
        private async Task<PlayUrlInfo> FetchPlayUrlDashAsync(string bvid, long cid, CancellationToken ct = default)
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
                response = await _client.GetAsync(requestUri, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // 412: Retry once
                await Task.Delay(1000, ct);
                response = await _client.GetAsync(requestUri, ct);
            }

            response.EnsureSuccessStatusCode();

            // ── Source-gen 零拷贝反序列化 ──
            using var dashStream = await response.Content.ReadAsStreamAsync(ct);
            var dashResponse = await JsonSerializer.DeserializeAsync(
                dashStream, BilibiliJsonContext.Default.DashPlayApiResponse, ct);

            if (dashResponse is null || dashResponse.Code != 0)
            {
                var msg = dashResponse?.Message ?? "获取播放地址失败。";
                throw new BilibiliApiException(msg);
            }

            var dashData = dashResponse.Data;
            if (dashData is null)
            {
                throw new BilibiliApiException("获取播放地址失败：响应缺少数据。");
            }

            // 从 DASH 格式获取音频流
            var audioList = dashData.Dash?.Audio;
            if (audioList is { Count: > 0 })
            {
                // 按 bandwidth 排序，取最高质量
                var bestAudio = audioList
                    .OrderByDescending(a => a.Bandwidth ?? 0)
                    .First();

                var url = bestAudio.BaseUrl
                          ?? bestAudio.BaseUrlFallback
                          ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(url))
                {
                    return new PlayUrlInfo
                    {
                        Url = url,
                        Quality = bestAudio.Bandwidth ?? 0,
                        Format = "m4s",
                        Size = 0,
                        Length = dashData.Timelength ?? 0,
                        IsAudioOnly = true,
                    };
                }
            }

            // A6: DASH 无可用音频 → MP4 fallback
            return await FetchPlayUrlMp4Async(bvid, cid, ct);
        }

        /// <summary>
        /// MP4 格式播放地址（fnval=1），传统直链。
        /// </summary>
        private async Task<PlayUrlInfo> FetchPlayUrlMp4Async(string bvid, long cid, CancellationToken ct = default)
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

            using var response = await _client.GetAsync(requestUri, ct);
            response.EnsureSuccessStatusCode();

            // ── Source-gen 零拷贝反序列化 ──
            using var mp4Stream = await response.Content.ReadAsStreamAsync(ct);
            var mp4Response = await JsonSerializer.DeserializeAsync(
                mp4Stream, BilibiliJsonContext.Default.Mp4PlayApiResponse, ct);

            if (mp4Response is null || mp4Response.Code != 0)
            {
                var msg = mp4Response?.Message ?? "获取播放地址失败。";
                throw new BilibiliApiException(msg);
            }

            var mp4Data = mp4Response.Data;
            if (mp4Data?.Durl is not { Count: > 0 })
            {
                throw new BilibiliApiException("获取播放地址失败：没有可用的视频流。");
            }

            var firstUrl = mp4Data.Durl[0];
            if (string.IsNullOrWhiteSpace(firstUrl.Url))
            {
                throw new BilibiliApiException("获取播放地址失败：播放地址为空。");
            }

            return new PlayUrlInfo
            {
                Url = firstUrl.Url,
                Quality = mp4Data.Quality ?? 64,
                Format = "mp4",
                Size = firstUrl.Size ?? 0,
                Length = firstUrl.Length ?? 0,
                IsAudioOnly = false,
            };
        }

        private static Uri BuildUri(string baseUrl, IReadOnlyDictionary<string, string> query)
        {
            // 使用 UriBuilder + StringBuilder 替代 List<string> + string.Join，
            // 减少中间数组分配
            var ub = new UriBuilder(baseUrl);
            var sb = new StringBuilder();
            foreach (var entry in query)
            {
                if (sb.Length > 0)
                {
                    sb.Append('&');
                }

                sb.Append(Uri.EscapeDataString(entry.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(entry.Value));
            }

            ub.Query = sb.ToString();
            return ub.Uri;
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

        /// <summary>
        /// 从 <see cref="JsonElement"/> 提取播放量字符串。
        /// API 可能返回数字（12345）或已格式化的字符串（"1.2万"），统一兼容。
        /// </summary>
        private static string ExtractPlayCount(JsonElement? element)
        {
            if (element is not { } el)
            {
                return "0";
            }

            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString() ?? "0",
                JsonValueKind.Number => el.GetInt64().ToString("N0"),
                _ => "0",
            };
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
