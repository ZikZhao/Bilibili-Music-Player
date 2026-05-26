using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.ViewModels;
using bilibili_music_player_windows;

namespace bilibili_music_player_windows.Pages
{
    public sealed partial class FavoritesPage : Page
    {
        public LibraryViewModel ViewModel { get; } = App.Current.GetService<LibraryViewModel>();

        public FavoritesPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 排序下拉框选择变更时触发排序。
        /// </summary>
        private void OnSortSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string option)
            {
                ViewModel.SortCommand.Execute(option);
            }
        }

        /// <summary>
        /// 点击收藏项时播放该视频。
        /// </summary>
        private void OnFavoriteItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is VideoModel video)
            {
                ViewModel.PlayFromFavoriteCommand.Execute(video);
            }
        }

        /// <summary>
        /// 点击删除按钮时移除收藏。
        /// </summary>
        private void OnRemoveFavoriteClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is VideoModel video)
            {
                ViewModel.RemoveFavoriteCommand.Execute(video);
            }
        }
    }
}
