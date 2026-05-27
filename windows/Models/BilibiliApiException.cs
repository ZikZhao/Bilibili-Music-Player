using System;

namespace bilibili_music_player_windows.Models
{
    public sealed class BilibiliApiException : Exception
    {
        public BilibiliApiException(string message)
            : base(message)
        {
        }

        public BilibiliApiException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
