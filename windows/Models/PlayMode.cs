namespace bilibili_music_player_windows.Models
{
    /// <summary>
    /// Windows 单一播放队列的两种模式；打乱是操作，不是播放模式。
    /// </summary>
    public enum PlayMode
    {
        /// <summary>
        /// 列表循环：播完当前后播下一首，全部播完回到第一首。
        /// </summary>
        Loop = 0,

        /// <summary>
        /// 单曲循环：播完当前后重播当前。
        /// </summary>
        Single = 1,

    }
}
