using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace bilibili_music_player_windows.Controls
{
    /// <summary>
    /// 视频统计数据栏，显示 5 个维度的统计信息：
    /// 播放、点赞、收藏数、硬币、时长。
    ///
    /// 所有数据通过 DependencyProperty 传入，支持 {x:Bind} 绑定。
    /// </summary>
    public sealed partial class VideoStatsBar : UserControl
    {
        public VideoStatsBar()
        {
            InitializeComponent();
        }

        /// <summary>播放量（格式化后，如 "12.3万"）。</summary>
        public string ViewCount
        {
            get => (string)GetValue(ViewCountProperty);
            set => SetValue(ViewCountProperty, value);
        }

        public static readonly DependencyProperty ViewCountProperty =
            DependencyProperty.Register(nameof(ViewCount), typeof(string), typeof(VideoStatsBar),
                new PropertyMetadata(string.Empty));

        /// <summary>点赞数（格式化后）。</summary>
        public string LikeCount
        {
            get => (string)GetValue(LikeCountProperty);
            set => SetValue(LikeCountProperty, value);
        }

        public static readonly DependencyProperty LikeCountProperty =
            DependencyProperty.Register(nameof(LikeCount), typeof(string), typeof(VideoStatsBar),
                new PropertyMetadata(string.Empty));

        /// <summary>收藏数（格式化后）。</summary>
        public string FavoriteCount
        {
            get => (string)GetValue(FavoriteCountProperty);
            set => SetValue(FavoriteCountProperty, value);
        }

        public static readonly DependencyProperty FavoriteCountProperty =
            DependencyProperty.Register(nameof(FavoriteCount), typeof(string), typeof(VideoStatsBar),
                new PropertyMetadata(string.Empty));

        /// <summary>硬币数（格式化后）。</summary>
        public string CoinCount
        {
            get => (string)GetValue(CoinCountProperty);
            set => SetValue(CoinCountProperty, value);
        }

        public static readonly DependencyProperty CoinCountProperty =
            DependencyProperty.Register(nameof(CoinCount), typeof(string), typeof(VideoStatsBar),
                new PropertyMetadata(string.Empty));

        /// <summary>时长（格式化后，如 "04:13"）。</summary>
        public string Duration
        {
            get => (string)GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register(nameof(Duration), typeof(string), typeof(VideoStatsBar),
                new PropertyMetadata(string.Empty));
    }
}
