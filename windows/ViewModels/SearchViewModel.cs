#pragma warning disable MVVMTK0045

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Api;
using bilibili_music_player_windows.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace bilibili_music_player_windows.ViewModels
{
    /// <summary>
    /// 搜索页面的状态枚举。
    /// </summary>
    public enum SearchState
    {
        /// <summary>初始空闲状态（无结果、无建议）。</summary>
        Idle,

        /// <summary>正在显示搜索建议下拉列表。</summary>
        ShowingSuggestions,

        /// <summary>正在执行搜索请求。</summary>
        Searching,

        /// <summary>搜索完成，显示结果。</summary>
        Results,

        /// <summary>搜索完成，但无结果。</summary>
        NoResults,

        /// <summary>搜索出错。</summary>
        Error,
    }

    /// <summary>
    /// 搜索页面 ViewModel，管理搜索文本、建议、历史、结果、分页等。
    ///
    /// ▸ 数据流：UI ←→ SearchViewModel ←→ BilibiliClient
    /// ▸ 搜索文本变化触发 300ms 防抖 → 获取建议
    /// ▸ 搜索历史自动保存到 ApplicationData.LocalSettings
    /// ▸ 分页通过 LoadMoreCommand 实现
    /// </summary>
    public partial class SearchViewModel : ObservableObject
    {
        private readonly BilibiliClient _client;
        private CancellationTokenSource? _debounceCts;

        private const int MaxHistoryItems = 10;
        private const string SearchHistoryKey = "search_history";

        // ── 可观察属性 ──

        /// <summary>搜索框当前文本（TwoWay 绑定）。</summary>
        [ObservableProperty]
        private string _searchText = string.Empty;

        /// <summary>搜索建议列表。</summary>
        [ObservableProperty]
        private ObservableCollection<SuggestionModel> _suggestions = new();

        /// <summary>搜索结果列表。</summary>
        [ObservableProperty]
        private ObservableCollection<VideoPreviewItem> _searchVideos = new();

        /// <summary>搜索历史关键词列表。</summary>
        [ObservableProperty]
        private ObservableCollection<string> _searchHistory = new();

        /// <summary>热门搜索关键词列表。</summary>
        [ObservableProperty]
        private ObservableCollection<string> _hotKeywords = new();

        /// <summary>是否正在搜索。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSearchingVisibility))]
        private bool _isSearching;

        /// <summary>错误消息（非 null 时显示错误 UI）。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ErrorMessageVisibility))]
        private string? _errorMessage;

        /// <summary>是否还有更多结果可分页加载。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasMoreVisibility))]
        private bool _hasMore;

        /// <summary>当前页码（从 1 开始）。</summary>
        [ObservableProperty]
        private int _currentPage = 1;

        /// <summary>搜索状态枚举，控制 UI 显示不同区域。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SuggestionsVisibility))]
        [NotifyPropertyChangedFor(nameof(IdleSectionVisibility))]
        [NotifyPropertyChangedFor(nameof(ResultsSectionVisibility))]
        private SearchState _searchState = SearchState.Idle;

        /// <summary>上一次搜索的关键词。</summary>
        [ObservableProperty]
        private string _lastKeyword = string.Empty;

        // ── 全屏预览状态 ──

        /// <summary>当前正在预览的视频项。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PreviewModeVisibility))]
        [NotifyPropertyChangedFor(nameof(SearchModeVisibility))]
        private VideoPreviewItem? _currentPreviewItem;

        /// <summary>是否处于全屏预览模式。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PreviewModeVisibility))]
        [NotifyPropertyChangedFor(nameof(SearchModeVisibility))]
        private bool _isPreviewing;

        /// <summary>当前预览项在搜索结果列表中的索引（用于高亮）。</summary>
        [ObservableProperty]
        private int _previewItemIndex = -1;

        /// <summary>搜索结果数量文本。</summary>
        [ObservableProperty]
        private int _totalResults;

        // ── 计算可见性属性 ──

        /// <summary>全屏预览模式的可见性。</summary>
        public Visibility PreviewModeVisibility => IsPreviewing
            ? Visibility.Visible
            : Visibility.Collapsed;

        /// <summary>搜索模式的可见性。</summary>
        public Visibility SearchModeVisibility => IsPreviewing
            ? Visibility.Collapsed
            : Visibility.Visible;

        /// <summary>搜索建议下拉列表的可见性。</summary>
        public Visibility SuggestionsVisibility => SearchState == SearchState.ShowingSuggestions
            ? Visibility.Visible
            : Visibility.Collapsed;

        /// <summary>初始区域（历史 + 热门）的可见性。</summary>
        public Visibility IdleSectionVisibility => SearchState is SearchState.Idle or SearchState.ShowingSuggestions
            ? Visibility.Visible
            : Visibility.Collapsed;

        /// <summary>搜索结果区域的可见性。</summary>
        public Visibility ResultsSectionVisibility => SearchState is SearchState.Results or SearchState.NoResults
            ? Visibility.Visible
            : Visibility.Collapsed;

        /// <summary>加载指示器的可见性。</summary>
        public Visibility IsSearchingVisibility => IsSearching
            ? Visibility.Visible
            : Visibility.Collapsed;

        /// <summary>加载更多按钮的可见性。</summary>
        public Visibility HasMoreVisibility => HasMore
            ? Visibility.Visible
            : Visibility.Collapsed;

        /// <summary>错误状态 UI 的可见性。</summary>
        public Visibility ErrorMessageVisibility => ErrorMessage is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

        // ── Constructor ──

        public SearchViewModel(BilibiliClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            LoadSearchHistory();
            InitializeHotKeywords();
        }

        // ── Hot keywords ──

        private void InitializeHotKeywords()
        {
            HotKeywords = new ObservableCollection<string>
            {
                "音乐",
                "演唱会",
                "钢琴",
                "吉他",
                "翻唱",
                "纯音乐",
                "古风",
                "电子音乐",
                "现场",
                "MV",
            };
        }

        // ── Search text debounce (S2) ──

        /// <summary>
        /// 当搜索文本变化时触发，实现 300ms 防抖。
        /// 文本长度 >= 2 时自动获取搜索建议。
        /// </summary>
        partial void OnSearchTextChanged(string value)
        {
            // 取消上一次防抖
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            if (string.IsNullOrWhiteSpace(value) || value.Length < 2)
            {
                Suggestions.Clear();
                SearchState = SearchState.Idle;
                return;
            }

            // 300ms 防抖后获取建议
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, token);

                    if (!token.IsCancellationRequested)
                    {
                        await FetchSuggestionsAsync(value);
                    }
                }
                catch (TaskCanceledException)
                {
                    // 被新输入取消，忽略
                }
            }, token);
        }

        /// <summary>
        /// 获取搜索建议（S2）。
        /// </summary>
        private async Task FetchSuggestionsAsync(string keyword)
        {
            try
            {
                var suggestions = await _client.FetchSuggestionsAsync(keyword);

                await Task.Run(() =>
                {
                    Suggestions.Clear();
                    foreach (var s in suggestions)
                    {
                        Suggestions.Add(s);
                    }
                });

                SearchState = suggestions.Count > 0
                    ? SearchState.ShowingSuggestions
                    : SearchState.Idle;
            }
            catch
            {
                // 建议接口失败静默处理
            }
        }

        // ── Commands ──

        /// <summary>
        /// 执行搜索（S1）。
        /// </summary>
        [RelayCommand]
        private async Task SearchAsync(string? keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            var trimmed = keyword.Trim();

            // 同步搜索框文本
            SearchText = trimmed;
            SearchState = SearchState.Searching;
            IsSearching = true;
            ErrorMessage = null;
            Suggestions.Clear();
            CurrentPage = 1;
            LastKeyword = trimmed;

            try
            {
                await AddToHistoryAsync(trimmed);

                var result = await _client.SearchVideosAsync(trimmed, CurrentPage);

                SearchVideos.Clear();
                foreach (var item in result.Videos)
                {
                    SearchVideos.Add(item);
                }

                HasMore = CurrentPage < result.NumPages;
                TotalResults = result.NumResults;
                SearchState = SearchVideos.Count > 0
                    ? SearchState.Results
                    : SearchState.NoResults;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                SearchState = SearchState.Error;
            }
            finally
            {
                IsSearching = false;
            }
        }

        /// <summary>
        /// 加载更多结果（分页，S4）。
        /// </summary>
        [RelayCommand]
        private async Task LoadMoreAsync()
        {
            if (IsSearching || !HasMore || string.IsNullOrWhiteSpace(LastKeyword))
            {
                return;
            }

            IsSearching = true;

            try
            {
                CurrentPage++;
                var result = await _client.SearchVideosAsync(LastKeyword, CurrentPage);

                foreach (var item in result.Videos)
                {
                    SearchVideos.Add(item);
                }

                HasMore = CurrentPage < result.NumPages;
            }
            catch (Exception ex)
            {
                CurrentPage--;
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsSearching = false;
            }
        }

        /// <summary>
        /// 进入全屏预览模式并切换到指定视频。
        /// </summary>
        public void EnterPreview(VideoPreviewItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            CurrentPreviewItem = item;
            PreviewItemIndex = SearchVideos.IndexOf(item);
            IsPreviewing = true;
        }

        /// <summary>
        /// 退出全屏预览模式。
        /// </summary>
        public void ExitPreview()
        {
            IsPreviewing = false;
            CurrentPreviewItem = null;
            PreviewItemIndex = -1;
        }

        /// <summary>
        /// 清空搜索历史（S5）。
        /// </summary>
        [RelayCommand]
        private async Task ClearHistoryAsync()
        {
            SearchHistory.Clear();
            await SaveHistoryAsync();
        }

        /// <summary>
        /// 重新搜索上一次关键词。
        /// </summary>
        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (!string.IsNullOrWhiteSpace(LastKeyword))
            {
                await SearchAsync(LastKeyword);
            }
        }

        /// <summary>
        /// 获取视频预览播放地址（委托给 BilibiliClient）。
        /// </summary>
        public Task<Uri> FetchPreviewUriAsync(string bvid)
        {
            return _client.FetchPreviewUriAsync(bvid);
        }

        // ── 搜索历史持久化 (S5) ──

        private void LoadSearchHistory()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(SearchHistoryKey, out var value) &&
                    value is string json &&
                    !string.IsNullOrWhiteSpace(json))
                {
                    var items = JsonSerializer.Deserialize<string[]>(json);
                    if (items != null)
                    {
                        SearchHistory.Clear();
                        foreach (var item in items)
                        {
                            SearchHistory.Add(item);
                        }
                    }
                }
            }
            catch
            {
                // 忽略历史加载失败
            }
        }

        private Task SaveHistoryAsync()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[SearchHistoryKey] = JsonSerializer.Serialize(SearchHistory.ToArray());
            }
            catch
            {
                // 忽略历史保存失败
            }

            return Task.CompletedTask;
        }

        private async Task AddToHistoryAsync(string keyword)
        {
            if (SearchHistory.Contains(keyword))
            {
                SearchHistory.Remove(keyword);
            }

            SearchHistory.Insert(0, keyword);

            while (SearchHistory.Count > MaxHistoryItems)
            {
                SearchHistory.RemoveAt(SearchHistory.Count - 1);
            }

            await SaveHistoryAsync();
        }
    }
}
