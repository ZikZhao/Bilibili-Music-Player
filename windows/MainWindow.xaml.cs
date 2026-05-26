using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using bilibili_music_player_windows.Pages;
using bilibili_music_player_windows.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace bilibili_music_player_windows
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly MainViewModel _mainViewModel;
        private readonly PlayerViewModel _playerViewModel;

        /// <summary>暴露给 XAML x:Bind 的 PlayerViewModel。</summary>
        public PlayerViewModel PlayerViewModel { get; }

        public MainWindow(MainViewModel mainViewModel, PlayerViewModel playerViewModel)
        {
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            _playerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));
            PlayerViewModel = _playerViewModel;
            InitializeComponent();
            NavList.SelectedIndex = 1;
            NavigateTo("favorites");
        }

        private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is ListViewItem item)
            {
                NavigateTo(item.Tag as string);
            }
        }

        private void NavigateTo(string? target)
        {
            Type? pageType = target switch
            {
                "search" => typeof(SearchPage),
                "favorites" => typeof(FavoritesPage),
                "settings" => typeof(SettingsPage),
                "nowplaying" => typeof(AudioPlayerPage),
                _ => null,
            };

            if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }

        /// <summary>导航到全屏播放器页面。</summary>
        private void OnNowPlayingClick(object sender, RoutedEventArgs e)
        {
            NavigateTo("nowplaying");
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
    }
}
