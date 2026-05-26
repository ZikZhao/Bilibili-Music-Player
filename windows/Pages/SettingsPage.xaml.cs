using Microsoft.UI.Xaml.Controls;
using bilibili_music_player_windows.ViewModels;

namespace bilibili_music_player_windows.Pages
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage()
        {
            ViewModel = App.Current.GetService<SettingsViewModel>();
            DataContext = ViewModel;
            InitializeComponent();
        }
    }
}
