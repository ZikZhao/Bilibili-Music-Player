#pragma warning disable MVVMTK0045
#pragma warning disable MVVMTK0034 // Direct field access intentional in LoadSettings()

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using bilibili_music_player_windows.Services;
using Windows.Storage;

namespace bilibili_music_player_windows.ViewModels
{
    /// <summary>
    /// 设置页面 ViewModel，管理应用设置、缓存管理。
    ///
    /// ▸ 数据流：UI ←→ SettingsViewModel ←→ ApplicationData.LocalSettings / CacheService / PlayerViewModel
    /// ▸ 所有设置自动持久化到 LocalSettings。
    /// ▸ 主题切换通过 <see cref="ThemeChanged"/> 事件通知 MainWindow 执行。
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly CacheService _cacheService;
        private readonly PlayerViewModel _playerViewModel;

        private const string ThemeModeKey = "ThemeMode";
        private const string EnableFadeKey = "EnableFade";
        private const string EnableAutoPlayKey = "EnableAutoPlay";

        /// <summary>
        /// 当主题模式变化时触发，由 MainWindow 订阅并应用 <see cref="ElementTheme"/>。
        /// </summary>
        public event EventHandler<string>? ThemeChanged;

        /// <summary>内部主题模式：System / Light / Dark。</summary>
        private string _themeMode = "System";

        /// <summary>当前主题模式（用于外部读取）。</summary>
        public string ThemeMode => _themeMode;

        // ── 可观察属性 ──

        /// <summary>主题选择索引（0=System, 1=Light, 2=Dark）。</summary>
        [ObservableProperty]
        private int _themeIndex = 0;

        /// <summary>渐变播放开关。</summary>
        [ObservableProperty]
        private bool _enableFade = true;

        /// <summary>自动播放开关。</summary>
        [ObservableProperty]
        private bool _enableAutoPlay = true;

        [ObservableProperty] private int _quickPlayGesture;
        [ObservableProperty] private int _quickPlayScope;
        [ObservableProperty] private int _quickPlayDestination;
        [ObservableProperty] private bool _enableAnimations = true;
        [ObservableProperty] private int _closeBehavior;
        [ObservableProperty] private bool _pauseInBackground;
        [ObservableProperty] private bool _enableAudioCache = true;

        /// <summary>缓存大小（格式化字符串，如 "42.9 MB"）。</summary>
        [ObservableProperty]
        private string _cacheSize = "计算中...";

        // ── Constructor ──

        public SettingsViewModel(CacheService cacheService, PlayerViewModel playerViewModel)
        {
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _playerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));
            LoadSettings();
            _ = RefreshCacheSizeAsync();
        }

        // ── 属性变化处理 ──

        /// <summary>
        /// 主题索引变化时更新主题模式、持久化并通知 MainWindow 应用主题。
        /// </summary>
        partial void OnThemeIndexChanged(int value)
        {
            var mode = value switch
            {
                1 => "Light",
                2 => "Dark",
                _ => "System",
            };

            if (_themeMode != mode)
            {
                _themeMode = mode;
                SaveSetting(ThemeModeKey, mode);
                ThemeChanged?.Invoke(this, mode);
                System.Diagnostics.Debug.WriteLine($"[SettingsVM] Theme changed: {mode}");
            }
        }

        /// <summary>
        /// 渐变开关变化时持久化并同步到 PlayerViewModel。
        /// </summary>
        partial void OnEnableFadeChanged(bool value)
        {
            SaveSetting(EnableFadeKey, value);
            _playerViewModel.EnableFade = value;
        }

        /// <summary>
        /// 自动播放开关变化时持久化。
        /// </summary>
        partial void OnEnableAutoPlayChanged(bool value)
        {
            SaveSetting(EnableAutoPlayKey, value);
        }

        partial void OnEnableAudioCacheChanged(bool value)
        { SaveSetting(nameof(EnableAudioCache), value); _playerViewModel.EnableAudioCache = value; }
        partial void OnQuickPlayGestureChanged(int value) => SaveSetting(nameof(QuickPlayGesture), value);
        partial void OnQuickPlayScopeChanged(int value) => SaveSetting(nameof(QuickPlayScope), value);
        partial void OnQuickPlayDestinationChanged(int value) => SaveSetting(nameof(QuickPlayDestination), value);
        partial void OnEnableAnimationsChanged(bool value) => SaveSetting(nameof(EnableAnimations), value);
        partial void OnCloseBehaviorChanged(int value) => SaveSetting(nameof(CloseBehavior), value);
        partial void OnPauseInBackgroundChanged(bool value) => SaveSetting(nameof(PauseInBackground), value);

        // ── 命令 ──

        /// <summary>
        /// 清除所有缓存。
        /// </summary>
        [RelayCommand]
        private async Task ClearCacheAsync()
        {
            await _cacheService.ClearAllAsync();
            await RefreshCacheSizeAsync();
        }

        // ── 持久化 ──

        /// <summary>
        /// 从 ApplicationData.LocalSettings 加载所有设置。
        /// </summary>
        private void LoadSettings()
        {
            var settings = ApplicationData.Current.LocalSettings;

            // 读取主题
            var savedTheme = settings.Values[ThemeModeKey] as string;
            _themeMode = savedTheme is "Light" or "Dark" ? savedTheme : "System";
            _themeIndex = _themeMode switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0,
            };

            // 读取其他设置
            bool ReadBool(string key, bool fallback) => settings.Values.TryGetValue(key, out var value) && value is bool flag ? flag : fallback;
            int ReadIndex(string key, int max) => settings.Values.TryGetValue(key, out var value) && value is int index && index >= 0 && index <= max ? index : 0;
            _enableFade = ReadBool(EnableFadeKey, true);
            _enableAutoPlay = ReadBool(EnableAutoPlayKey, true);
            _quickPlayGesture = ReadIndex(nameof(QuickPlayGesture), 2);
            _quickPlayScope = ReadIndex(nameof(QuickPlayScope), 1);
            _quickPlayDestination = ReadIndex(nameof(QuickPlayDestination), 2);
            _enableAnimations = ReadBool(nameof(EnableAnimations), true);
            _closeBehavior = ReadIndex(nameof(CloseBehavior), 1);
            _pauseInBackground = ReadBool(nameof(PauseInBackground), false);
            _enableAudioCache = ReadBool(nameof(EnableAudioCache), true);

            // 同步到 PlayerViewModel
            _playerViewModel.EnableFade = EnableFade;
            _playerViewModel.EnableAudioCache = EnableAudioCache;
        }

        /// <summary>
        /// 保存单条设置到 LocalSettings。
        /// </summary>
        private static void SaveSetting(string key, object value)
        {
            ApplicationData.Current.LocalSettings.Values[key] = value;
        }

        /// <summary>
        /// 刷新缓存大小显示。
        /// </summary>
        public async Task RefreshCacheSizeAsync()
        {
            try
            {
                CacheSize = await _cacheService.GetFormattedCacheSizeAsync();
            }
            catch
            {
                CacheSize = "未知";
            }
        }
    }
}
