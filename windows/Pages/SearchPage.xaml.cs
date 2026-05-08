using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using bilibili_music_player_windows.ViewModels;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Pages
{
    public sealed partial class SearchPage : Page
    {
        public MainViewModel ViewModel { get; } = MainViewModel.Instance;

        public SearchPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PreviewDialog.XamlRoot = XamlRoot;
        }

        private async void OnVideoClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not VideoPreviewItem item)
            {
                return;
            }

            PreviewTitleText.Text = item.Title;
            PreviewMetaText.Text = $"{item.Author} · {item.Views}";
            PreviewPlayer.Source = MediaSource.CreateFromUri(item.PreviewUri);

            await PreviewDialog.ShowAsync();
        }

        private void OnPreviewDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
        {
            PreviewPlayer.Source = null;
        }
    }
}
