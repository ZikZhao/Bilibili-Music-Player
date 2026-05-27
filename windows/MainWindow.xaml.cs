using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using bilibili_music_player_windows.Pages;
using bilibili_music_player_windows.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace bilibili_music_player_windows
{
    /// <summary>
    /// Main window with NavigationView sidebar, Mica backdrop (U4),
    /// custom title bar (U5), and theme-aware layout.
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

            // U4: Enable MicaBackdrop
            SystemBackdrop = new MicaBackdrop();

            // U5: Custom title bar
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // ST3: 订阅主题变化
            _settingsViewModel.ThemeChanged += OnThemeChanged;
            ApplyTheme(_settingsViewModel.ThemeMode);

            // Set initial navigation to favorites
            NavView.SelectedItem = NavView.MenuItems[2]; // "我的收藏"
            NavigateTo("favorites");
        }

        /// <summary>主题变化时延迟应用。</summary>
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

        /// <summary>
        /// U1: Handle NavigationView selection changes.
        /// </summary>
        private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer is NavigationViewItem item)
            {
                NavigateTo(item.Tag?.ToString());
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

        /// <summary>绕过 Slider 内部 Thumb 的事件截获，监听底层指针事件。</summary>
        private void OnProgressSliderLoaded(object sender, RoutedEventArgs e)
        {
            // 创建并绑定 ThumbToolTip 值转换器（绕过 XAML 资源树的逻辑树断裂）
            var timeConverter = new Converters.ProgressToTimeConverter();
            var durationBinding = new Microsoft.UI.Xaml.Data.Binding
            {
                Source = _playerViewModel,
                Path = new PropertyPath(nameof(PlayerViewModel.Duration)),
                Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay,
            };
            Microsoft.UI.Xaml.Data.BindingOperations.SetBinding(
                timeConverter, Converters.ProgressToTimeConverter.TotalDurationProperty, durationBinding);
            ProgressSlider.ThumbToolTipValueConverter = timeConverter;

            ProgressSlider.AddHandler(
                UIElement.PointerPressedEvent,
                new PointerEventHandler(OnProgressSliderPointerPressed),
                handledEventsToo: true);

            ProgressSlider.AddHandler(
                UIElement.PointerCaptureLostEvent,
                new PointerEventHandler(OnProgressSliderPointerCaptureLost),
                handledEventsToo: true);
        }

        private void OnProgressSliderPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _playerViewModel.IsUserSeeking = true;
        }

        private void OnProgressSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Slider slider)
            {
                _playerViewModel.SeekCommand.Execute(slider.Value);
            }

            _playerViewModel.IsUserSeeking = false;
        }
    }
}
