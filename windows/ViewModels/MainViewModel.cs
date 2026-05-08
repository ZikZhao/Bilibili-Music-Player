using System;
using System.Collections.ObjectModel;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.ViewModels
{
    public sealed class MainViewModel
    {
        public static MainViewModel Instance { get; } = new MainViewModel();

        public ObservableCollection<TrackItem> Favorites { get; } = new();
        public ObservableCollection<VideoPreviewItem> SearchVideos { get; } = new();
        public ObservableCollection<string> SearchHistory { get; } = new();
        public ObservableCollection<string> HotKeywords { get; } = new();

        private MainViewModel()
        {
            Seed();
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

            SearchVideos.Add(new VideoPreviewItem
            {
                Title = "星降る海 (Live Version)",
                Author = "夏レモン",
                Duration = "04:38",
                Views = "23.8万观看",
                CoverUri = new Uri("https://picsum.photos/seed/bili1/480/270"),
                PreviewUri = new Uri("https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"),
            });
            SearchVideos.Add(new VideoPreviewItem
            {
                Title = "願い～あの頃のキミへ～",
                Author = "Kumorine",
                Duration = "05:41",
                Views = "12.4万观看",
                CoverUri = new Uri("https://picsum.photos/seed/bili2/480/270"),
                PreviewUri = new Uri("https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/Sintel.mp4"),
            });
            SearchVideos.Add(new VideoPreviewItem
            {
                Title = "光るなら (OP Edit)",
                Author = "JLRS-jayfm",
                Duration = "04:10",
                Views = "8.1万观看",
                CoverUri = new Uri("https://picsum.photos/seed/bili3/480/270"),
                PreviewUri = new Uri("https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ElephantsDream.mp4"),
            });

            SearchHistory.Add("星降之海");
            SearchHistory.Add("若能绽放光芒");
            SearchHistory.Add("refrain");
            SearchHistory.Add("secrets");

            HotKeywords.Add("祈愿 致那个时候的你");
            HotKeywords.Add("星降る海");
            HotKeywords.Add("光るなら");
            HotKeywords.Add("Re:frain");
        }
    }
}
