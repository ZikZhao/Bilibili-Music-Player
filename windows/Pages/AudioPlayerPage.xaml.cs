using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using System;
using bilibili_music_player_windows.Converters;
using bilibili_music_player_windows.ViewModels;
using bilibili_music_player_windows.Services;

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
    public sealed partial class AudioPlayerPage : Page, IResidentTab, IDisposable
    {
        public PlayerViewModel ViewModel { get; }
        private Controls.PlaybackSeekBinding? _seekBinding;
        public event RoutedEventHandler? PlaylistRequested;

        public AudioPlayerPage()
        {
            ViewModel = App.Current.GetService<PlayerViewModel>();
            DataContext = ViewModel;
            InitializeComponent();

        }

        private void OnShowPlaylistClick(object sender, RoutedEventArgs e) => PlaylistRequested?.Invoke(this, e);
        public void SetTabActive(bool active) => CoverBackdrop.SetActive(active);
        public void Dispose() { _seekBinding?.Dispose(); CoverBackdrop.Dispose(); }
#if DEBUG
        internal object InspectBackground() => CoverBackdrop.Inspect();
#endif

        /// <summary>音量百分比文本。</summary>
        public static string VolumePercent(double volume) =>
            $"{VolumePercentConverter.Format(volume)}%";

        /// <summary>
        /// P8: 根据 PlayMode 枚举值返回对应图标可见性。
        /// <paramref name="modeValue"/>: 0=Loop, 1=Single。
        /// </summary>
        public static Visibility PlayModeToVisibility(Models.PlayMode mode, int modeValue)
        {
            return (int)mode == modeValue ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnPlayerPageLoaded(object sender, RoutedEventArgs e)
        {
            _seekBinding?.Dispose();
            _seekBinding = new Controls.PlaybackSeekBinding(PlayerPageProgressSlider, ViewModel);
        }
    }
}
