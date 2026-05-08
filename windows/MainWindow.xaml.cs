using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using bilibili_music_player_windows.Pages;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace bilibili_music_player_windows
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            NavList.SelectedIndex = 1;
            NavigateTo("favorites");
        }

        private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is ListViewItem item)
            {
                NavigateTo(item.Tag as string);
            }
        }

        private void NavigateTo(string? target)
        {
            Type? pageType = target switch
            {
                "search" => typeof(SearchPage),
                "favorites" => typeof(FavoritesPage),
                "settings" => typeof(SettingsPage),
                _ => null,
            };

            if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }
}
