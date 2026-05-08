using Microsoft.UI.Xaml.Controls;
using bilibili_music_player_windows.ViewModels;

namespace bilibili_music_player_windows.Pages
{
    public sealed partial class FavoritesPage : Page
    {
        public MainViewModel ViewModel { get; } = MainViewModel.Instance;

        public FavoritesPage()
        {
            InitializeComponent();
        }
    }
}
