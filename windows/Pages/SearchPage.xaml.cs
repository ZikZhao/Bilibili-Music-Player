using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Media.Core;
using Windows.System;
using bilibili_music_player_windows;
using bilibili_music_player_windows.Api;
using bilibili_music_player_windows.ViewModels;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Pages
{
    public sealed partial class SearchPage : Page
    {
        public MainViewModel ViewModel { get; } = App.Current.GetService<MainViewModel>();

        private static readonly HttpClient PreviewHttpClient = CreatePreviewHttpClient();
        private HttpRangeStream? _previewStream;

        public SearchPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            ViewModel.SearchHistory.CollectionChanged += (_, _) => UpdateHistoryState();
            UpdateHistoryState();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PreviewDialog.XamlRoot = XamlRoot;
        }

        private void UpdateHistoryState()
        {
            if (ClearHistoryButton == null)
            {
                return;
            }

            ClearHistoryButton.IsEnabled = ViewModel.SearchHistory.Count > 0;
        }

        private async Task ExecuteSearchAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            try
            {
                await ViewModel.SearchAsync(keyword);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("搜索失败", ex.Message);
            }
        }

        private static HttpClient CreatePreviewHttpClient()
        {
            var handler = new HttpClientHandler();

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15),
            };

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.bilibili.com");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            client.DefaultRequestHeaders.AcceptEncoding.Clear();
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity");

            return client;
        }

        private async Task<MediaSource> CreatePreviewSourceAsync(Uri uri)
        {
            _previewStream?.Dispose();
            _previewStream = new HttpRangeStream(PreviewHttpClient, uri);
            await _previewStream.InitializeAsync();
            var randomAccessStream = _previewStream.AsRandomAccessStream();
            return MediaSource.CreateFromStream(randomAccessStream, _previewStream.ContentType);
        }

        private async Task ShowErrorAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot,
            };

            await dialog.ShowAsync();
        }

        private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            await ExecuteSearchAsync(SearchBox.Text);
        }

        private async void OnKeywordClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not string keyword)
            {
                return;
            }

            SearchBox.Text = keyword;
            await ExecuteSearchAsync(keyword);
        }

        private async void OnClearHistoryClick(object sender, RoutedEventArgs e)
        {
            await ViewModel.ClearHistoryAsync();
            UpdateHistoryState();
        }

        private async void OnVideoClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not VideoPreviewItem item)
            {
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(item.Bvid))
                {
                    throw new InvalidOperationException("缺少视频标识。请重新搜索后重试。");
                }

                var previewUri = await ViewModel.FetchPreviewUriAsync(item.Bvid);
                var previewSource = await CreatePreviewSourceAsync(previewUri);
                PreviewTitleText.Text = item.Title;
                PreviewMetaText.Text = $"{item.Author} · {item.Views}";
                PreviewPlayer.Source = previewSource;

                await PreviewDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("播放失败", ex.Message);
            }
        }

        private void OnPreviewDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
        {
            PreviewPlayer.Source = null;
            _previewStream?.Dispose();
            _previewStream = null;
        }
    }
}
