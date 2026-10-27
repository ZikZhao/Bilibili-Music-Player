using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using bilibili_music_player_windows.Services;
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
        private readonly SearchViewModel _searchViewModel;
        private readonly LibraryViewModel _libraryViewModel;
        private readonly ResidentTabs<Page>? _tabs;

        /// <summary>暴露给 XAML x:Bind 的 PlayerViewModel。</summary>
        public PlayerViewModel PlayerViewModel { get; }

        public MainWindow(
            MainViewModel mainViewModel,
            PlayerViewModel playerViewModel,
            SettingsViewModel settingsViewModel,
            SearchViewModel searchViewModel,
            LibraryViewModel libraryViewModel)
        {
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            _playerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));
            _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
            _searchViewModel = searchViewModel;
            _libraryViewModel = libraryViewModel;
            PlayerViewModel = _playerViewModel;
            InitializeComponent();
            _tabs = new ResidentTabs<Page>(CreateTab, SetTabActive);
            Closed += OnWindowClosed;

            // U4: Enable MicaBackdrop
            SystemBackdrop = new MicaBackdrop();

            // U5: Custom title bar
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            _searchViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SearchViewModel.CanGoBack)) UpdateBackButton();
            };
            _libraryViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(LibraryViewModel.CanGoBack)) UpdateBackButton();
            };
            var backShortcut = new KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.Left,
                Modifiers = Windows.System.VirtualKeyModifiers.Menu,
            };
            backShortcut.Invoked += (_, args) =>
            {
                args.Handled = TryGoBack();
            };
            ((FrameworkElement)Content).KeyboardAccelerators.Add(backShortcut);
            InitializeWindowLifetime();

            // ST3: 订阅主题变化
            _settingsViewModel.ThemeChanged += OnThemeChanged;
            ApplyTheme(_settingsViewModel.ThemeMode);

            // Set initial navigation to favorites
            NavView.SelectedItem = NavView.MenuItems[2]; // "我的收藏"
            NavigateTo("favorites");
#if DEBUG
            StartUiDiagnostics();
#endif
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
            if (_tabs is null || target is not ("search" or "favorites" or "settings" or "nowplaying")) return;
            _tabs.Select(target);
            foreach (var entry in NavView.MenuItems)
                if (entry is NavigationViewItem item && item.Tag?.ToString() == target &&
                    !ReferenceEquals(NavView.SelectedItem, item)) NavView.SelectedItem = item;
            UpdateBackButton();
        }

        private Page CreateTab(string key)
        {
            Page page = key switch
            {
                "search" => new SearchPage(),
                "favorites" => new FavoritesPage(),
                "settings" => new SettingsPage(),
                "nowplaying" => new AudioPlayerPage(),
                _ => throw new ArgumentOutOfRangeException(nameof(key)),
            };
            page.Visibility = Visibility.Collapsed;
            PageHost.Children.Add(page);
            return page;
        }

        private static void SetTabActive(Page page, bool active)
        {
            // Pause before hiding. The page stays parented, so neither Loaded nor
            // Unloaded is used as the tab activation signal.
            if (active) page.Visibility = Visibility.Visible;
            (page as IResidentTab)?.SetTabActive(active);
            page.IsHitTestVisible = active;
            if (!active) page.Visibility = Visibility.Collapsed;
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _tray?.Dispose();
            try { _tabs?.Dispose(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Tabs] Window cleanup: {ex}"); }
        }

        private void UpdateBackButton()
        {
            TitleBackButton.IsEnabled = _tabs?.ActivePage switch
            {
                SearchPage => _searchViewModel.CanGoBack,
                FavoritesPage => _libraryViewModel.CanGoBack,
                _ => false,
            };
            var label = _tabs?.ActivePage is FavoritesPage ? "返回上一级" :
                _searchViewModel.IsPreviewing ? "返回搜索结果" : "返回搜索历史";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TitleBackButton, label);
            ToolTipService.SetToolTip(TitleBackButton, $"{label} (Esc / Alt+←)");
        }

        private void OnTitleBackClick(object sender, RoutedEventArgs e)
        {
            TryGoBack();
        }

        /// <summary>导航到全屏播放器页面。</summary>
        private void OnNowPlayingClick(object sender, RoutedEventArgs e)
        {
            NavigateTo("nowplaying");
        }

    }
}
