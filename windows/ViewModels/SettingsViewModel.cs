#pragma warning disable MVVMTK0045

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using bilibili_music_player_windows.Services;

namespace bilibili_music_player_windows.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly CacheService _cacheService;

        public SettingsViewModel(CacheService cacheService)
        {
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            // Load cache size asynchronously on construction
            _ = LoadCacheSizeAsync();
        }

        [ObservableProperty]
        private string _themeMode = "System";

        [ObservableProperty]
        private bool _enableFade = true;

        [ObservableProperty]
        private bool _enableAutoPlay = true;

        [ObservableProperty]
        private string _audioQuality = "High";

        [ObservableProperty]
        private string _cacheSize = "0 MB";

        [RelayCommand]
        private async Task ClearCacheAsync()
        {
            await _cacheService.ClearAllAsync();
            await LoadCacheSizeAsync();
        }

        private async Task LoadCacheSizeAsync()
        {
            try
            {
                CacheSize = await _cacheService.GetFormattedCacheSizeAsync();
            }
            catch
            {
                CacheSize = "Unknown";
            }
        }
    }
}
