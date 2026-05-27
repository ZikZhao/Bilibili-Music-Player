using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
        private readonly LibraryViewModel _libraryViewModel;
        private HttpRangeStream? _previewStream;
        private bool _isPreviewMediaLoaded;

        public SearchPage()
        {
            ViewModel = App.Current.GetService<SearchViewModel>();
            _libraryViewModel = App.Current.GetService<LibraryViewModel>();
            DataContext = ViewModel;
            _httpClient = App.Current.GetService<HttpClient>();
            InitializeComponent();
            Loaded += OnLoaded;
            ViewModel.SearchHistory.CollectionChanged += (_, _) => UpdateHistoryState();
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateHistoryState();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            MainScrollViewer.ViewChanged += OnScrollViewChanged;
        }

        /// <summary>
        /// 页面离开时彻底清理预览状态 — 停止播放、释放资源、退出预览。
        /// </summary>
        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ForceStopPreview();
        }

        /// <summary>
        /// 强制停止预览 — 无论是否在预览模式，都清理媒体并重置状态。
        /// </summary>
        private void ForceStopPreview()
        {
            // 停止播放器
            if (_isPreviewMediaLoaded)
            {
                PreviewPlayer.MediaPlayer.Pause();
                PreviewPlayer.Source = null;
                _isPreviewMediaLoaded = false;
            }

            // 释放网络流
            _previewStream?.Dispose();
            _previewStream = null;

            // 退出预览状态
            if (ViewModel.IsPreviewing)
            {
                ViewModel.ExitPreview();
            }
        }

        /// <summary>
        /// 监听 ViewModel 属性变化，当退出预览时清理媒体资源。
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SearchViewModel.IsPreviewing) && !ViewModel.IsPreviewing)
            {
                CleanupPreviewMedia();
            }
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

        /// <summary>
        /// 点击搜索结果 — 进入全屏预览模式。
        /// </summary>
        private async void OnVideoClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not VideoPreviewItem item)
            {
                return;
            }

            await StartPreviewAsync(item);
        }

        /// <summary>
        /// 在预览模式的侧边列表中点击某条结果 — 切换到该视频。
        /// </summary>
        private async void OnPreviewResultClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not VideoPreviewItem item)
            {
                return;
            }

            // 如果点击的已经是当前播放项，不做任何事
            if (ViewModel.CurrentPreviewItem?.Bvid == item.Bvid)
            {
                return;
            }

            await StartPreviewAsync(item);
        }

        /// <summary>
        /// 关闭预览 — 退出全屏预览模式回到搜索界面。
        /// </summary>
        private void OnClosePreviewClick(object sender, RoutedEventArgs e)
        {
            ViewModel.ExitPreview();
        }

        /// <summary>
        /// 加载并播放指定视频，进入全屏预览模式。
        /// </summary>
        private async Task StartPreviewAsync(VideoPreviewItem item)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.Bvid))
                {
                    throw new InvalidOperationException("缺少视频标识。请重新搜索后重试。");
                }

                // 立即停止旧播放器并清除源，避免切换时旧视频继续播放
                if (_isPreviewMediaLoaded)
                {
                    PreviewPlayer.MediaPlayer.Pause();
                    PreviewPlayer.Source = null;
                    _isPreviewMediaLoaded = false;
                }
                _previewStream?.Dispose();
                _previewStream = null;

                // 进入预览模式（显示 UI 骨架）
                ViewModel.EnterPreview(item);
                UpdatePreviewInfo(item);

                var previewUri = await ViewModel.FetchPreviewUriAsync(item.Bvid);
                var previewSource = await CreatePreviewSourceAsync(previewUri);
                PreviewPlayer.Source = previewSource;
                _isPreviewMediaLoaded = true;

                // 同步选中列表中对应项
                SyncPreviewSelection();
            }
            catch (Exception ex)
            {
                // 加载失败时退出预览模式并提示
                ViewModel.ExitPreview();
                await ShowErrorAsync("播放失败", ex.Message);
            }
        }

        /// <summary>
        /// 更新右侧视频信息面板的显示内容。
        /// </summary>
        private void UpdatePreviewInfo(VideoPreviewItem item)
        {
            PreviewTitleText.Text = item.Title;
            PreviewAuthorText.Text = item.Author;
            PreviewMetaText.Text = $"UP 主";
            PreviewViewsText.Text = item.Views;
            PreviewDurationText.Text = item.Duration;
            UpdateFavoriteButtonState(item);
        }

        /// <summary>
        /// 收藏按钮点击 — 切换当前预览视频的收藏状态。
        /// </summary>
        private async void OnToggleFavoriteClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ViewModel.CurrentPreviewItem is not VideoPreviewItem item)
                {
                    return;
                }

                var videoModel = VideoModel.FromPreviewItem(item);

                if (_libraryViewModel.ToggleFavoriteCommand is null)
                {
                    System.Diagnostics.Debug.WriteLine("[SearchPage] ToggleFavoriteCommand is null!");
                    return;
                }

                await _libraryViewModel.ToggleFavoriteCommand.ExecuteAsync(videoModel);
                UpdateFavoriteButtonState(item);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SearchPage] ToggleFavorite failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据收藏状态更新收藏按钮的图标和文字。
        /// </summary>
        private void UpdateFavoriteButtonState(VideoPreviewItem item)
        {
            var isFavorited = _libraryViewModel.Favorites.Any(f => f.Bvid == item.Bvid);
            if (isFavorited)
            {
                FavoriteButtonIcon.Symbol = Symbol.SolidStar;
                FavoriteButtonText.Text = "已收藏";
                AutomationProperties.SetName(FavoriteButton, "取消收藏");
            }
            else
            {
                FavoriteButtonIcon.Symbol = Symbol.OutlineStar;
                FavoriteButtonText.Text = "收藏";
                AutomationProperties.SetName(FavoriteButton, "收藏视频");
            }
        }

        /// <summary>
        /// 同步侧边列表的选中项到当前播放的视频。
        /// </summary>
        private void SyncPreviewSelection()
        {
            if (ViewModel.PreviewItemIndex >= 0 &&
                ViewModel.PreviewItemIndex < PreviewSearchResultsList.Items.Count)
            {
                PreviewSearchResultsList.SelectedIndex = ViewModel.PreviewItemIndex;
                PreviewSearchResultsList.ScrollIntoView(
                    PreviewSearchResultsList.Items[ViewModel.PreviewItemIndex]);
            }
        }

        /// <summary>
        /// 清理预览媒体资源（保留预览模式，只清理播放器）。
        /// </summary>
        private void CleanupPreviewMedia()
        {
            if (_isPreviewMediaLoaded)
            {
                PreviewPlayer.MediaPlayer.Pause();
                PreviewPlayer.Source = null;
                _isPreviewMediaLoaded = false;
            }

            _previewStream?.Dispose();
            _previewStream = null;
        }
    }
}
