namespace bilibili_music_player_windows.Models
{
    public sealed class TrackItem
    {
        public int Index { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Artist { get; init; } = string.Empty;
        public string Duration { get; init; } = string.Empty;
    }
}
