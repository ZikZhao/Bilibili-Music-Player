using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using bilibili_music_player_windows.ViewModels;

namespace bilibili_music_player_windows.Pages
{
    /// <summary>
    /// 全屏音频播放器页面（P6）。
    ///
    /// ▸ 封面、标题、艺术家
    /// ▸ 进度条 + 时间标签
    /// ▸ 控制按钮：播放模式 | 上一首 | 播放/暂停 | 下一首 | 播放列表
    /// ▸ 音量控制
    /// ▸ 播放列表侧边栏（P7）
    /// </summary>
    public sealed partial class AudioPlayerPage : Page
    {
        public PlayerViewModel ViewModel { get; }

        public AudioPlayerPage()
        {
            ViewModel = App.Current.GetService<PlayerViewModel>();
            DataContext = ViewModel;
            InitializeComponent();
        }

        // ── P7: 切换播放列表面板 ──

        private void OnTogglePlaylistClick(object sender, RoutedEventArgs e)
        {
            PlaylistPanel.Visibility =
                PlaylistPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        // ── x:Bind 辅助方法 ──

        /// <summary>格式化 TimeSpan 为 mm:ss 或 h:mm:ss。</summary>
        public static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts <= TimeSpan.Zero) return "--:--";
            return ts.Hours > 0
                ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        public static Visibility BoolToVisibility(bool value) =>
            value ? Visibility.Visible : Visibility.Collapsed;

        public static Visibility InvertBoolToVisibility(bool value) =>
            value ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// P8: 根据 PlayMode 枚举值返回对应图标可见性。
        /// <paramref name="modeValue"/>: 0=Loop, 1=Single, 2=Shuffle。
        /// </summary>
        public static Visibility PlayModeToVisibility(Models.PlayMode mode, int modeValue)
        {
            return (int)mode == modeValue ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>音量百分比文本。</summary>
        public static string VolumePercent(double volume) =>
            $"{(int)(Math.Clamp(volume, 0, 1) * 100)}%";
    }
}
