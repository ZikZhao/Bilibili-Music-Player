namespace bilibili_music_player_windows.Models
{
    /// <summary>
    /// 播放地址信息，包含 URL、格式、码率等。
    /// </summary>
    public sealed class PlayUrlInfo
    {
        /// <summary>
        /// 播放 URL（视频流或音频流）。
        /// </summary>
        public string Url { get; init; } = string.Empty;

        /// <summary>
        /// 音频 URL（仅 DASH 视频流时可能用到）。
        /// </summary>
        public string? AudioUrl { get; init; }

        /// <summary>
        /// 画质 ID 或音频带宽（bps）。
        /// </summary>
        public int Quality { get; init; }

        /// <summary>
        /// 容器格式（"m4s" / "mp4"）。
        /// </summary>
        public string Format { get; init; } = string.Empty;

        /// <summary>
        /// 文件大小（字节）。
        /// </summary>
        public long Size { get; init; }

        /// <summary>
        /// 时长（毫秒）。
        /// </summary>
        public long Length { get; init; }

        /// <summary>
        /// 是否为纯音频流。
        /// </summary>
        public bool IsAudioOnly { get; init; }

        /// <summary>
        /// 获取画质名称。
        /// </summary>
        public string QualityName => Quality switch
        {
            16 => "360P",
            32 => "480P",
            64 => "720P",
            80 => "1080P",
            112 => "1080P+",
            116 => "1080P60",
            120 => "4K",
            125 => "HDR",
            126 => "杜比视界",
            127 => "8K",
            _ => "未知",
        };
    }
}
