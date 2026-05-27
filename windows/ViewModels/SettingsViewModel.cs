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

        /// <summary>音频质量（当前为只读显示）。</summary>
        [ObservableProperty]
        private string _audioQuality = "高 (192K)";

        /// <summary>缓存大小（格式化字符串，如 "42.9 MB"）。</summary>
        [ObservableProperty]
        private string _cacheSize = "计算中...";

        // ── Constructor ──

        public SettingsViewModel(CacheService cacheService, PlayerViewModel playerViewModel)
        {
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _playerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            LoadSettings();
            await RefreshCacheSizeAsync();
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
        /// 设置主题（由外观分段控件的 Click 事件调用）。
        /// </summary>
        public void SetTheme(int index)
        {
            ThemeIndex = index;
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
            var savedTheme = (string?)settings.Values[ThemeModeKey];
            _themeMode = savedTheme ?? "System";
            _themeIndex = _themeMode switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0,
            };

            // 读取其他设置
            _enableFade = (bool?)(settings.Values[EnableFadeKey] ?? true) ?? true;
            _enableAutoPlay = (bool?)(settings.Values[EnableAutoPlayKey] ?? true) ?? true;

            // 同步到 PlayerViewModel
            _playerViewModel.EnableFade = EnableFade;
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
        private async Task RefreshCacheSizeAsync()
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
