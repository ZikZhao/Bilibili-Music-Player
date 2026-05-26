namespace bilibili_music_player_windows.Models
{
    /// <summary>
    /// 播放模式枚举，与移动端 <c>PlayMode</c> 一致。
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

        /// <summary>
        /// 随机播放：播完当前后随机选一首（不会与当前相同）。
        /// </summary>
        Shuffle = 2,
    }
}
