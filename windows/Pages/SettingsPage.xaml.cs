using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using bilibili_music_player_windows.ViewModels;
using Windows.UI;

namespace bilibili_music_player_windows.Pages
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }

        private static readonly SolidColorBrush RoseBrush = new(Color.FromArgb(0xFF, 0xFB, 0x71, 0x85));
        private static readonly SolidColorBrush WhiteBrush = new(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush TransparentBrush = new(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
        private static readonly SolidColorBrush Slate600Brush = new(Color.FromArgb(0xFF, 0x47, 0x55, 0x69));

        public SettingsPage()
        {
            ViewModel = App.Current.GetService<SettingsViewModel>();
            DataContext = ViewModel;
            InitializeComponent();

            // Apply the initial theme visual state
            UpdateThemeVisual();
        }

        private void OnThemeAutoClick(object sender, RoutedEventArgs e)
        {
            ViewModel.SetTheme(0);
            UpdateThemeVisual();
        }

        private void OnThemeLightClick(object sender, RoutedEventArgs e)
        {
            ViewModel.SetTheme(1);
            UpdateThemeVisual();
        }

        private void OnThemeDarkClick(object sender, RoutedEventArgs e)
        {
            ViewModel.SetTheme(2);
            UpdateThemeVisual();
        }

        private void UpdateThemeVisual()
        {
            var index = ViewModel.ThemeIndex;

            ThemeAutoBg.Background = index == 0 ? RoseBrush : TransparentBrush;
            ThemeAutoText.Foreground = index == 0 ? WhiteBrush : Slate600Brush;

            ThemeLightBg.Background = index == 1 ? RoseBrush : TransparentBrush;
            ThemeLightText.Foreground = index == 1 ? WhiteBrush : Slate600Brush;

            ThemeDarkBg.Background = index == 2 ? RoseBrush : TransparentBrush;
            ThemeDarkText.Foreground = index == 2 ? WhiteBrush : Slate600Brush;
        }
    }
}
