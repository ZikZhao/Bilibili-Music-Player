using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace bilibili_music_player_windows.Api
{
    // ──────────────────────────────
    //  API 响应 DTO Records（极简结构）
    //  配合 System.Text.Json Source Generator 实现零拷贝反序列化
    // ──────────────────────────────

    // ── Search ──

    internal record SearchApiResponse(
        int Code,
        string? Message,
        SearchResponseData? Data
    );

    internal record SearchResponseData(
        List<SearchVideoItem>? Result,
        int? Page,
        int? Pagesize,
        int? NumResults,
        int? NumPages,
        JsonElement? V_voucher
    );

    internal record SearchVideoItem(
        string? Bvid,
        string? Title,
        string? Author,
        string? Duration,
        JsonElement? Play,   // API 可能返回数字（12345）或字符串（"1.2万"），用 JsonElement 兼容
        string? Pic
    );

    // ── Video Detail ──

    internal record VideoDetailApiResponse(
        int Code,
        string? Message,
        VideoDetailData? Data
    );

    internal record VideoDetailData(
        string? Bvid,
        long? Aid,
        long? Cid,
        string? Title,
        string? Desc,
        [property: JsonPropertyName("pic")] string? Cover,
        VideoOwnerData? Owner,
        VideoStatData? Stat,
        long? Pubdate,
        long? Duration,
        long? Videos,
        List<VideoPageData>? Pages
    );

    internal record VideoOwnerData(
        long? Mid,
        string? Name,
        string? Face
    );

    internal record VideoStatData(
        long? View,
        long? Danmaku,
        long? Reply,
        long? Favorite,
        long? Coin,
        long? Share,
        long? Like
    );

    internal record VideoPageData(
        long? Cid
    );

    // ── DASH Play URL ──

    internal record DashPlayApiResponse(
        int Code,
        string? Message,
        DashPlayData? Data
    );

    internal record DashPlayData(
        DashFormatData? Dash,
        long? Timelength
    );

    internal record DashFormatData(
        List<AudioStreamData>? Audio
    );

    internal record AudioStreamData(
        int? Bandwidth,
        [property: JsonPropertyName("baseUrl")] string? BaseUrl,
        [property: JsonPropertyName("base_url")] string? BaseUrlFallback,
        int? Codecid
    );

    // ── MP4 Play URL ──

    internal record Mp4PlayApiResponse(
        int Code,
        string? Message,
        Mp4PlayData? Data
    );

    internal record Mp4PlayData(
        List<DurlItemData>? Durl,
        int? Quality
    );

    internal record DurlItemData(
        string? Url,
        long? Size,
        long? Length
    );

    // ── Nav / WBI Keys ──

    internal record NavApiResponse(
        int Code,
        string? Message,
        NavData? Data
    );

    internal record NavData(
        [property: JsonPropertyName("wbi_img")] WbiImgData? WbiImg
    );

    internal record WbiImgData(
        [property: JsonPropertyName("img_url")] string? ImgUrl,
        [property: JsonPropertyName("sub_url")] string? SubUrl
    );

    // ── Suggest ──

    internal record SuggestApiResponse(
        int Code,
        string? Message,
        SuggestResultData? Result
    );

    internal record SuggestResultData(
        List<SuggestTagData>? Tag
    );

    internal record SuggestTagData(
        string? Value,
        string? Name
    );

    // ── JsonSerializerContext（编译期生成反序列化代码） ──

    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip)]
    [JsonSerializable(typeof(SearchApiResponse))]
    [JsonSerializable(typeof(VideoDetailApiResponse))]
    [JsonSerializable(typeof(DashPlayApiResponse))]
    [JsonSerializable(typeof(Mp4PlayApiResponse))]
    [JsonSerializable(typeof(NavApiResponse))]
    [JsonSerializable(typeof(SuggestApiResponse))]
    internal partial class BilibiliJsonContext : JsonSerializerContext
    {
    }
}
