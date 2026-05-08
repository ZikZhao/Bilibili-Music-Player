using System;

namespace bilibili_music_player_windows.Models
{
    public sealed class VideoPreviewItem
    {
        public string Title { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public string Duration { get; init; } = string.Empty;
        public string Views { get; init; } = string.Empty;
        public Uri CoverUri { get; init; } = new Uri("https://picsum.photos/seed/bili/480/270");
        public Uri PreviewUri { get; init; } = new Uri("https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4");
    }
}
