using Microsoft.UI.Xaml.Controls;
using bilibili_music_player_windows.ViewModels;
using bilibili_music_player_windows;

namespace bilibili_music_player_windows.Pages
{
    public sealed partial class FavoritesPage : Page
    {
        public MainViewModel ViewModel { get; } = App.Current.GetService<MainViewModel>();

        public FavoritesPage()
        {
            InitializeComponent();
        }
    }
}
