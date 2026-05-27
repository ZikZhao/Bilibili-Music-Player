using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using System;
using bilibili_music_player_windows.Converters;
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

            // 创建并绑定 ThumbToolTip 值转换器（绕过 XAML 资源树的逻辑树断裂）
            var timeConverter = new ProgressToTimeConverter();
            var durationBinding = new Binding
            {
                Source = ViewModel,
                Path = new PropertyPath(nameof(PlayerViewModel.Duration)),
                Mode = BindingMode.OneWay,
            };
            BindingOperations.SetBinding(timeConverter, ProgressToTimeConverter.TotalDurationProperty, durationBinding);
            PlayerPageProgressSlider.ThumbToolTipValueConverter = timeConverter;
        }

        // ── P7: 切换播放列表面板 ──

        private void OnTogglePlaylistClick(object sender, RoutedEventArgs e)
        {
            PlaylistPanel.Visibility =
                PlaylistPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        /// <summary>音量百分比文本。</summary>
        public static string VolumePercent(double volume) =>
            $"{(int)(Math.Clamp(volume, 0, 1) * 100)}%";

        /// <summary>
        /// P8: 根据 PlayMode 枚举值返回对应图标可见性。
        /// <paramref name="modeValue"/>: 0=Loop, 1=Single, 2=Shuffle。
        /// </summary>
        public static Visibility PlayModeToVisibility(Models.PlayMode mode, int modeValue)
        {
            return (int)mode == modeValue ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>绕过 Slider 内部 Thumb 的事件截获，监听底层指针事件。</summary>
        private void OnPlayerPageLoaded(object sender, RoutedEventArgs e)
        {
            PlayerPageProgressSlider.AddHandler(
                UIElement.PointerPressedEvent,
                new PointerEventHandler(OnProgressSliderPointerPressed),
                handledEventsToo: true);

            PlayerPageProgressSlider.AddHandler(
                UIElement.PointerCaptureLostEvent,
                new PointerEventHandler(OnProgressSliderPointerCaptureLost),
                handledEventsToo: true);
        }

        private void OnProgressSliderPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            ViewModel.IsUserSeeking = true;
        }

        private void OnProgressSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Slider slider)
            {
                ViewModel.SeekCommand.Execute(slider.Value);
            }

            ViewModel.IsUserSeeking = false;
        }
    }
}
