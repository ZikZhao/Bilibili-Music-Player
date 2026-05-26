#pragma warning disable MVVMTK0045

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bilibili_music_player_windows.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
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
        private void ClearCache() { }
    }
}
