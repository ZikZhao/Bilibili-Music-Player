using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Media.Core;
using Windows.System;
using bilibili_music_player_windows.Api;
using bilibili_music_player_windows.ViewModels;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Pages
{
    public sealed partial class SearchPage : Page
    {
        public SearchViewModel ViewModel { get; }

        private readonly HttpClient _httpClient;
        private HttpRangeStream? _previewStream;

        public SearchPage()
        {
            ViewModel = App.Current.GetService<SearchViewModel>();
            DataContext = ViewModel;
            _httpClient = App.Current.GetService<HttpClient>();
            InitializeComponent();
            Loaded += OnLoaded;
            ViewModel.SearchHistory.CollectionChanged += (_, _) => UpdateHistoryState();
            UpdateHistoryState();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PreviewDialog.XamlRoot = XamlRoot;
            MainScrollViewer.ViewChanged += OnScrollViewChanged;
        }

        /// <summary>
        /// 滚动检测：当靠近底部 200px 时触发加载更多（S4）。
        /// </summary>
        private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e.IsIntermediate || sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            var scrollableHeight = scrollViewer.ScrollableHeight;
            if (scrollableHeight <= 0)
            {
                return;
            }

            var threshold = 200.0;
            if (scrollViewer.VerticalOffset >= scrollableHeight - threshold)
            {
                if (ViewModel.HasMore && !ViewModel.IsSearching)
                {
                    ViewModel.LoadMoreCommand.Execute(null);
                }
            }
        }

        private void UpdateHistoryState()
        {
            if (ClearHistoryButton is null)
            {
                return;
            }

            ClearHistoryButton.IsEnabled = ViewModel.SearchHistory.Count > 0;
        }

        private void ExecuteSearch(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            ViewModel.SearchCommand.Execute(keyword);
        }

        private async Task<MediaSource> CreatePreviewSourceAsync(Uri uri)
        {
            _previewStream?.Dispose();
            _previewStream = new HttpRangeStream(_httpClient, uri);
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

        private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            ExecuteSearch(SearchBox.Text);
        }

        private void OnKeywordClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not string keyword)
            {
                return;
            }

            SearchBox.Text = keyword;
            ExecuteSearch(keyword);
        }

        /// <summary>
        /// 点击搜索建议项（S3）。
        /// </summary>
        private void OnSuggestionClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SuggestionModel suggestion && !string.IsNullOrWhiteSpace(suggestion.Value))
            {
                SearchBox.Text = suggestion.Value;
                ExecuteSearch(suggestion.Value);
            }
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
