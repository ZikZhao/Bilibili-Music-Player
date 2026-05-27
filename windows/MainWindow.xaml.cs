using Microsoft.UI.Dispatching;
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
        private readonly SettingsViewModel _settingsViewModel;

        /// <summary>暴露给 XAML x:Bind 的 PlayerViewModel。</summary>
        public PlayerViewModel PlayerViewModel { get; }

        public MainWindow(
            MainViewModel mainViewModel,
            PlayerViewModel playerViewModel,
            SettingsViewModel settingsViewModel)
        {
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            _playerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));
            _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
            PlayerViewModel = _playerViewModel;
            InitializeComponent();

            // ST3: 订阅主题变化 — MainWindow 负责应用 theme，不经过 ViewModel
            _settingsViewModel.ThemeChanged += OnThemeChanged;
            // 应用初始主题（如果已保存）
            ApplyTheme(_settingsViewModel.ThemeMode);

            NavList.SelectedIndex = 1;
            NavigateTo("favorites");
        }

        /// <summary>主题变化时延迟应用，避免在事件传播过程中重绘视觉树导致原生崩溃。</summary>
        private void OnThemeChanged(object? sender, string mode)
        {
            var dispatcher = DispatcherQueue;
            if (dispatcher is null) return;

            dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => ApplyTheme(mode));
        }

        /// <summary>应用 <c>ElementTheme</c> 到窗口的根 FrameworkElement。</summary>
        private void ApplyTheme(string mode)
        {
            if (Content is not FrameworkElement rootElement) return;

            rootElement.RequestedTheme = mode switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            System.Diagnostics.Debug.WriteLine($"[MainWindow] Theme applied: {mode}");
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

    }
}
