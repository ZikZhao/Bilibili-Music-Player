using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using Windows.System;
using bilibili_music_player_windows.Api;
using bilibili_music_player_windows.ViewModels;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Services;

namespace bilibili_music_player_windows.Pages
{
    public sealed partial class SearchPage : Page, IResidentTab, IDisposable
    {
        public SearchViewModel ViewModel { get; }

        private readonly HttpClient _httpClient;
        private HttpRangeStream? _previewStream;
        private IRandomAccessStream? _previewRandomAccessStream;
        private MediaSource? _previewSource;
        private MediaPlayer? _previewMediaPlayer;
        private CancellationTokenSource? _previewLoadCts;
        private bool _isActive;
        private bool _isTabActive;
        private bool _isLoaded;
        private bool _isDisposed;
        private VideoPreviewItem? _requestedPreviewItem;
        private SearchStage _visibleStage;
        private int _visibleResultVersion = -1;
        private bool _wasSearching;
        private TimeSpan _pendingStartPosition;

        public SearchPage()
        {
            ViewModel = App.Current.GetService<SearchViewModel>();
            DataContext = ViewModel;
            _httpClient = App.Current.GetService<HttpClient>();
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SearchPageRoot.AddHandler(UIElement.PointerPressedEvent,
                new PointerEventHandler(OnPagePointerPressed), handledEventsToo: true);
            UpdateHistoryState();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isDisposed) return;
            _isLoaded = true;
            MainScrollViewer.ViewChanged -= OnScrollViewChanged;
            MainScrollViewer.ViewChanged += OnScrollViewChanged;
            ViewModel.SearchHistory.CollectionChanged -= OnSearchHistoryChanged;
            ViewModel.SearchHistory.CollectionChanged += OnSearchHistoryChanged;
            ViewModel.Session.StateChanged -= OnSessionStateChanged;
            ViewModel.Session.StateChanged += OnSessionStateChanged;
            ApplyTabActivation();
        }

        // Top-level tab switches never unload this page. Only the window's actual
        // visual-tree teardown reaches here.
        private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();

        public void SetTabActive(bool active)
        {
            _isTabActive = active;
            if (_isLoaded && !_isDisposed) ApplyTabActivation();
        }

        private void ApplyTabActivation()
        {
            _isActive = _isTabActive;
            ViewModel.SetActive(_isActive);
            if (!_isActive)
            {
                if (_previewMediaPlayer is { } player)
                {
                    player.Pause();
                    SavePlaybackSettings(player);
                    ViewModel.Session.PreviewPosition = player.PlaybackSession.Position;
                }
                if (PreviewPlayer.IsFullWindow) PreviewPlayer.IsFullWindow = false;
                ViewModel.IsPreviewLoading = false;
                return;
            }

            // Keep the same player, source, buffered data and controls. A pending
            // request may also have completed while this tab was hidden.
            ViewModel.IsPreviewLoading = ViewModel.IsPreviewing &&
                (_previewLoadCts is not null || _previewMediaPlayer?.PlaybackSession.PlaybackState is
                    MediaPlaybackState.Opening or MediaPlaybackState.Buffering);
            ReconcileSession();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _isActive = _isTabActive = _isLoaded = false;
            MainScrollViewer.ViewChanged -= OnScrollViewChanged;
            ViewModel.SearchHistory.CollectionChanged -= OnSearchHistoryChanged;
            ViewModel.Session.StateChanged -= OnSessionStateChanged;
            ViewModel.SetActive(false);
            CancelPreviewLoad();
            CleanupPreviewMedia();
            ViewModel.IsPreviewLoading = false;
            _requestedPreviewItem = null;
        }

        private void OnSearchHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateHistoryState();

        private void OnPreviewLayoutSizeChanged(object sender, SizeChangedEventArgs e)
        {
            PreviewListColumn.Width = new GridLength(Math.Clamp(e.NewSize.Width * 0.34, 220, 360));
            PreviewMetadataScrollViewer.MaxHeight = e.NewSize.Height < 480 ? 140 : 220;
        }

        private void OnSearchHeaderSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSuggestionsLayout();
        }

        private void UpdateSuggestionsLayout()
        {
            if (!ViewModel.HasSuggestions || SearchBox.ActualWidth <= 0) return;
            var anchor = SearchBox.TransformToVisual(SearchPageRoot)
                .TransformPoint(new Windows.Foundation.Point(0, SearchBox.ActualHeight + 4));
            Canvas.SetLeft(SuggestionsBorder, anchor.X);
            Canvas.SetTop(SuggestionsBorder, anchor.Y);
            SuggestionsBorder.Width = SearchBox.ActualWidth;
            SuggestionsList.MaxHeight = Math.Max(60, Math.Min(300, SearchPageRoot.ActualHeight - anchor.Y - 16));
            for (var index = 0; index < SuggestionsList.Items.Count; index++)
                if (SuggestionsList.ContainerFromIndex(index) is ListViewItem { ContentTemplateRoot: TextBlock text })
                    AlignSuggestionText(text);
        }

        private void OnSuggestionTextLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock text) AlignSuggestionText(text);
        }

        private void AlignSuggestionText(TextBlock text)
        {
            // Measure the actual container inset instead of duplicating the
            // native TextBox or ListViewItem template's padding constants.
            var currentLeft = text.TransformToVisual(SuggestionsBorder).TransformPoint(new Windows.Foundation.Point()).X;
            var inset = Math.Max(0, text.Margin.Left + SearchBox.TextStartOffset - currentLeft);
            text.Margin = new Thickness(inset, 8, 12, 8);
        }

        private void OnPagePointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isActive || !ViewModel.HasSuggestions) return;
            var entryPoint = e.GetCurrentPoint(SearchBox).Position;
            var listPoint = e.GetCurrentPoint(SuggestionsBorder).Position;
            if (entryPoint.X >= 0 && entryPoint.Y >= 0 && entryPoint.X <= SearchBox.ActualWidth && entryPoint.Y <= SearchBox.ActualHeight) return;
            if (listPoint.X >= 0 && listPoint.Y >= 0 && listPoint.X <= SuggestionsBorder.ActualWidth && listPoint.Y <= SuggestionsBorder.ActualHeight) return;
            ViewModel.DismissSuggestions();
        }

        /// <summary>
        /// 页面历史变化后同步媒体和矩阵滚动位置。
        /// </summary>
        private void OnSessionStateChanged(object? sender, EventArgs e) => ReconcileSession();

        private void ReconcileSession()
        {
            if (!_isActive) return;
            UpdateSuggestionsLayout();
            if (!ViewModel.IsPreviewing)
            {
                CancelPreviewLoad();
                CleanupPreviewMedia();
                _requestedPreviewItem = null;
                if (ViewModel.Stage == SearchStage.Results && !ViewModel.IsSearching &&
                    (_visibleStage != SearchStage.Results || _visibleResultVersion != ViewModel.Session.ResultVersion || _wasSearching))
                {
                    var version = ViewModel.Session.ResultVersion;
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        if (!_isActive || ViewModel.Stage != SearchStage.Results || version != ViewModel.Session.ResultVersion) return;
                        MainScrollViewer.UpdateLayout();
                        MainScrollViewer.ChangeView(null, ViewModel.Session.ResultsScrollOffset, null, disableAnimation: true);
                    });
                }
            }
            else if (ViewModel.CurrentPreviewItem is { } item && !ReferenceEquals(_requestedPreviewItem, item))
                _ = StartPreviewAsync(item, startPosition: ViewModel.Session.PreviewPosition);

            _visibleStage = ViewModel.Stage;
            _visibleResultVersion = ViewModel.Session.ResultVersion;
            _wasSearching = ViewModel.IsSearching;
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

            if (!_isActive || ViewModel.Stage != SearchStage.Results || ViewModel.IsSearching) return;
            ViewModel.Session.ResultsScrollOffset = scrollViewer.VerticalOffset;

            var scrollableHeight = scrollViewer.ScrollableHeight;
            if (scrollableHeight <= 0)
            {
                return;
            }

            var threshold = 200.0;
            if (scrollViewer.VerticalOffset >= scrollableHeight - threshold)
            {
                if (ViewModel.HasMore && !ViewModel.IsSearching && !ViewModel.IsLoadingMore && ViewModel.LoadMoreError is null)
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

            if (_previewMediaPlayer is { } player) ViewModel.Session.PreviewPosition = player.PlaybackSession.Position;
            ViewModel.SearchCommand.Execute(keyword);
        }

        private void CancelPreviewLoad()
        {
            var previous = _previewLoadCts;
            _previewLoadCts = null;
            previous?.Cancel();
            // The request that created the CTS disposes it after its continuations finish.
        }

        private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key is VirtualKey.Down or VirtualKey.Up && ViewModel.HasSuggestions)
            {
                var step = e.Key == VirtualKey.Down ? 1 : -1;
                SuggestionsList.SelectedIndex = (SuggestionsList.SelectedIndex + step + ViewModel.Suggestions.Count) % ViewModel.Suggestions.Count;
                SuggestionsList.ScrollIntoView(SuggestionsList.SelectedItem);
                e.Handled = true;
                return;
            }
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            if (ViewModel.HasSuggestions && SuggestionsList.SelectedItem is SuggestionModel selected) SearchBox.Text = selected.Value;
            ExecuteSearch(SearchBox.Text);
        }

        private void OnSearchButtonClick(object sender, RoutedEventArgs e) => ExecuteSearch(SearchBox.Text);
        private void OnSuggestionsKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter && SuggestionsList.SelectedItem is SuggestionModel item)
            {
                e.Handled = true;
                SearchBox.Text = item.Value;
                ExecuteSearch(item.Value);
            }
        }

        private void OnKeywordClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string keyword || string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            SearchBox.Text = keyword;
            ViewModel.DismissSuggestions();
            SearchBox.Focus(FocusState.Programmatic);
            SearchBox.SelectionStart = SearchBox.Text.Length;
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
                ViewModel.DismissSuggestions();
                SearchBox.Focus(FocusState.Programmatic);
                SearchBox.SelectionStart = SearchBox.Text.Length;
            }
        }

        /// <summary>
        /// 点击搜索结果 — 进入全屏预览模式（V1）。
        /// </summary>
        private void OnVideoClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not VideoPreviewItem item)
            {
                return;
            }

            ViewModel.Session.ResultsScrollOffset = MainScrollViewer.VerticalOffset;
            ViewModel.EnterPreview(item);
        }

        /// <summary>
        /// 在预览模式的侧边列表中点击某条结果 — 切换到该视频（V1）。
        /// </summary>
        private void OnPreviewResultClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not VideoPreviewItem item)
            {
                return;
            }

            // 如果点击的已经是当前播放项，不做任何事
            if (ViewModel.CurrentPreviewItem?.Bvid == item.Bvid && ViewModel.PreviewErrorMessage is null)
            {
                return;
            }

            ViewModel.EnterPreview(item);
        }

        /// <summary>
        /// 加载并播放指定视频，进入全屏预览模式（V1）。
        /// 每次加载拥有独立的取消令牌和媒体资源，只有当前请求可以接管播放器。
        /// </summary>
        private async Task StartPreviewAsync(VideoPreviewItem item, bool autoPlay = true, TimeSpan startPosition = default)
        {
            if (!_isActive) return;
            CancelPreviewLoad();
            CleanupPreviewMedia();
            _requestedPreviewItem = item;
            _pendingStartPosition = startPosition;
            ViewModel.PreviewErrorMessage = null;
            ViewModel.IsPreviewLoading = true;
            using var request = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            _previewLoadCts = request;
            var ct = request.Token;
            HttpRangeStream? stream = null;
            IRandomAccessStream? randomAccessStream = null;
            MediaSource? source = null;
            MediaPlayer? player = null;
            SyncPreviewSelection();

            try
            {
                if (string.IsNullOrWhiteSpace(item.Bvid))
                {
                    throw new InvalidOperationException("缺少视频标识。请重新搜索后重试。");
                }

                var uri = await ViewModel.LoadPreviewAsync(item, ct);
                ct.ThrowIfCancellationRequested();
                stream = new HttpRangeStream(_httpClient, uri);
                await stream.InitializeAsync(ct);
                ct.ThrowIfCancellationRequested();
                if (_isDisposed || !ViewModel.IsPreviewing || !ReferenceEquals(_previewLoadCts, request)) return;

                randomAccessStream = stream.AsRandomAccessStream();
                source = MediaSource.CreateFromStream(randomAccessStream, stream.ContentType);
                player = new MediaPlayer { Volume = ViewModel.Session.PreviewVolume, IsMuted = ViewModel.Session.PreviewMuted };
                player.MediaOpened += OnPreviewMediaOpened;
                player.MediaFailed += OnPreviewMediaFailed;
                player.PlaybackSession.PlaybackStateChanged += OnPreviewPlaybackStateChanged;
                _previewStream = stream;
                _previewRandomAccessStream = randomAccessStream;
                _previewSource = source;
                _previewMediaPlayer = player;
                stream = null;
                randomAccessStream = null;
                source = null;
                player = null;
                PreviewPlayer.SetMediaPlayer(_previewMediaPlayer);
                PreviewPlayer.Source = _previewSource;
                if (autoPlay && _isActive) _previewMediaPlayer.Play();
            }
            catch (OperationCanceledException) when (!ReferenceEquals(_previewLoadCts, request) || _isDisposed)
            {
                // Replacing the preview, backing out, or closing the window cancels it.
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_previewLoadCts, request) && !_isDisposed && ViewModel.IsPreviewing)
                {
                    CleanupPreviewMedia();
                    ViewModel.IsPreviewLoading = false;
                    ViewModel.PreviewErrorMessage = ex switch
                    {
                        OperationCanceledException => "视频加载超时，请重试。",
                        HttpRequestException { StatusCode: HttpStatusCode.PreconditionFailed } =>
                            "B 站暂时限制了本次播放请求（412），请稍后重试。",
                        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } =>
                            "播放地址已失效或被拒绝（403），请重试获取新地址。",
                        _ => $"视频加载失败：{ex.Message}",
                    };
                }
            }
            finally
            {
                player?.Dispose();
                source?.Dispose();
                randomAccessStream?.Dispose();
                stream?.Dispose();
                if (ReferenceEquals(_previewLoadCts, request)) _previewLoadCts = null;
            }
        }

        private async void OnRetryPreviewClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentPreviewItem is { } item) await StartPreviewAsync(item);
        }

        private void OnPreviewMediaOpened(MediaPlayer sender, object args) =>
            UpdatePreviewOnUIThread(sender, () =>
            {
                sender.PlaybackSession.PlaybackRate = ViewModel.Session.PreviewPlaybackRate;
                if (_pendingStartPosition > TimeSpan.Zero && sender.PlaybackSession.CanSeek)
                    sender.PlaybackSession.Position = TimeSpan.FromTicks(Math.Min(
                        _pendingStartPosition.Ticks, sender.PlaybackSession.NaturalDuration.Ticks));
                _pendingStartPosition = TimeSpan.Zero;
                if (!_isActive) sender.Pause();
                ViewModel.IsPreviewLoading = false;
            });

        private void OnPreviewPlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed || !ViewModel.IsPreviewing || _previewMediaPlayer is not { } player ||
                    !ReferenceEquals(sender, player.PlaybackSession)) return;
                if (!_isActive && sender.PlaybackState == MediaPlaybackState.Playing) player.Pause();
                ViewModel.IsPreviewLoading = _isActive && sender.PlaybackState is
                    MediaPlaybackState.Opening or MediaPlaybackState.Buffering;
            });
        }

        private void OnPreviewMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            var message = string.IsNullOrWhiteSpace(args.ErrorMessage) ? args.Error.ToString() : args.ErrorMessage;
            UpdatePreviewOnUIThread(sender, () =>
            {
                CleanupPreviewMedia();
                ViewModel.IsPreviewLoading = false;
                ViewModel.PreviewErrorMessage = $"视频播放失败，请重试。{message}";
            });
        }

        private void UpdatePreviewOnUIThread(MediaPlayer player, Action update)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isDisposed && ViewModel.IsPreviewing && ReferenceEquals(player, _previewMediaPlayer)) update();
            });
        }

        /// <summary>
        /// 同步侧边列表的选中项到当前播放的视频。
        /// </summary>
        private void SyncPreviewSelection()
        {
            if (ViewModel.CurrentPreviewItem is { } item)
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (!_isActive || !ViewModel.IsPreviewing || !ReferenceEquals(ViewModel.CurrentPreviewItem, item)) return;
                    PreviewSearchResultsList.UpdateLayout();
                    PreviewSearchResultsList.SelectedItem = item;
                    PreviewSearchResultsList.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
                });
            }
        }

        /// <summary>
        /// 清理预览媒体资源（保留预览模式，只清理播放器）。
        /// </summary>
        private void CleanupPreviewMedia()
        {
            if (PreviewPlayer.IsFullWindow) PreviewPlayer.IsFullWindow = false;
            if (_previewMediaPlayer is { } player)
            {
                _previewMediaPlayer = null;
                player.MediaOpened -= OnPreviewMediaOpened;
                player.MediaFailed -= OnPreviewMediaFailed;
                player.PlaybackSession.PlaybackStateChanged -= OnPreviewPlaybackStateChanged;
                SavePlaybackSettings(player);
                player.Pause();
                PreviewPlayer.Source = null;
                PreviewPlayer.SetMediaPlayer(null);
                player.Dispose();
            }
            _previewSource?.Dispose();
            _previewSource = null;
            _previewRandomAccessStream?.Dispose();
            _previewRandomAccessStream = null;
            _previewStream?.Dispose();
            _previewStream = null;
        }

        private void SavePlaybackSettings(MediaPlayer player)
        {
            ViewModel.Session.PreviewVolume = player.Volume;
            ViewModel.Session.PreviewMuted = player.IsMuted;
            ViewModel.Session.PreviewPlaybackRate = player.PlaybackSession.PlaybackRate;
        }

    }
}
