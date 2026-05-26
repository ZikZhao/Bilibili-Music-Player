#pragma warning disable MVVMTK0045

using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Api;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage;

namespace bilibili_music_player_windows.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const int MaxHistoryItems = 10;
        private const string SearchHistoryKey = "search_history";

        private readonly BilibiliClient _client;

        [ObservableProperty]
        private ObservableCollection<TrackItem> _favorites = new();

        [ObservableProperty]
        private ObservableCollection<VideoPreviewItem> _searchVideos = new();

        [ObservableProperty]
        private ObservableCollection<string> _searchHistory = new();

        [ObservableProperty]
        private ObservableCollection<string> _hotKeywords = new();

        public MainViewModel(BilibiliClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            Seed();
            LoadSearchHistory();
        }

        private void Seed()
        {
            Favorites.Add(new TrackItem
            {
                Index = 1,
                Title = "超高清4k音质无损【官方MV】[超时空辉夜姬] 星降る海",
                Artist = "夏レモン",
                Duration = "04:13",
            });
            Favorites.Add(new TrackItem
            {
                Index = 2,
                Title = "“能遇上这首歌，算你有本事……” | 《願い～あの頃のキミへ～》",
                Artist = "Kumorine",
                Duration = "05:41",
            });
            Favorites.Add(new TrackItem
            {
                Index = 3,
                Title = "若能绽放光芒 | 《四月是你的谎言》OP《光るなら》",
                Artist = "JLRS-jayfm",
                Duration = "04:10",
            });

            HotKeywords.Add("祈愿 致那个时候的你");
            HotKeywords.Add("星降る海");
            HotKeywords.Add("光るなら");
            HotKeywords.Add("Re:frain");
        }

        /// <summary>
        /// Executes a search for the given keyword and populates <see cref="SearchVideos"/>.
        /// </summary>
        public async Task SearchAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            var trimmed = keyword.Trim();

            await AddToHistoryAsync(trimmed);

            var result = await _client.SearchVideosAsync(trimmed);

            SearchVideos.Clear();
            foreach (var item in result.Videos)
            {
                SearchVideos.Add(item);
            }
        }

        /// <summary>
        /// Clears the search history and persists the empty state.
        /// </summary>
        public async Task ClearHistoryAsync()
        {
            SearchHistory.Clear();
            await SaveHistoryAsync();
        }

        public Task<Uri> FetchPreviewUriAsync(string bvid)
        {
            return _client.FetchPreviewUriAsync(bvid);
        }

        private void LoadSearchHistory()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(SearchHistoryKey, out var value) && value is string json && !string.IsNullOrWhiteSpace(json))
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
                // Ignore history load failures.
            }
        }

        private Task SaveHistoryAsync()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[SearchHistoryKey] = JsonSerializer.Serialize(SearchHistory);
            }
            catch
            {
                // Ignore history save failures.
            }

            return Task.CompletedTask;
        }

        private Task AddToHistoryAsync(string keyword)
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

            return SaveHistoryAsync();
        }
    }
}
