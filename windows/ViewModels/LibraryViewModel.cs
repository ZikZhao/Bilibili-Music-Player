#pragma warning disable MVVMTK0045

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using bilibili_music_player_windows.Api;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace bilibili_music_player_windows.ViewModels
{
    /// <summary>
    /// 收藏库 ViewModel，管理收藏列表、排序、持久化、自动下载。
    ///
    /// ▸ 数据流：UI ←→ LibraryViewModel ←→ PlayerViewModel / BilibiliClient / CacheService
    /// ▸ 收藏列表自动保存到 favorites.json（ApplicationData.LocalFolder）
    /// ▸ 添加到收藏时自动触发后台下载（L4）
    /// </summary>
    public partial class LibraryViewModel : ObservableObject
    {
        private readonly PlayerViewModel _playerViewModel;
        private readonly BilibiliClient _bilibiliClient;
        private readonly CacheService _cacheService;

        private const string FavoritesFileName = "favorites.json";

        // ── 可观察属性 ──

        /// <summary>收藏的视频列表。</summary>
        [ObservableProperty]
        private ObservableCollection<VideoModel> _favorites = new();

        /// <summary>当前排序方式。</summary>
        [ObservableProperty]
        private string _sortOption = "收藏时间";

        /// <summary>可用的排序选项。</summary>
        [ObservableProperty]
        private ObservableCollection<string> _sortOptions = new() { "收藏时间", "标题" };

        /// <summary>收藏列表是否为空。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
        [NotifyPropertyChangedFor(nameof(ListVisibility))]
        private bool _isEmpty = true;

        /// <summary>空状态提示的可见性。</summary>
        public Visibility EmptyStateVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>收藏列表的可见性（为空时隐藏）。</summary>
        public Visibility ListVisibility => IsEmpty ? Visibility.Collapsed : Visibility.Visible;

        // ── Constructor ──

        public LibraryViewModel(
            PlayerViewModel playerViewModel,
            BilibiliClient bilibiliClient,
            CacheService cacheService)
        {
            _playerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));
            _bilibiliClient = bilibiliClient ?? throw new ArgumentNullException(nameof(bilibiliClient));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));

            _ = LoadFavoritesAsync();
        }

        // ── 当收藏列表变化时更新 Empty 状态 ──

        partial void OnFavoritesChanged(ObservableCollection<VideoModel> value)
        {
            IsEmpty = value.Count == 0;
        }

        // ── 命令 ──

        /// <summary>
        /// 切换收藏状态：如果已收藏则移除，否则添加。
        /// 不触发下载——下载推迟到播放时由 PlayerViewModel 处理。
        /// </summary>
        [RelayCommand]
        private async Task ToggleFavoriteAsync(VideoModel? video)
        {
            if (video is null)
            {
                return;
            }

            var existing = Favorites.FirstOrDefault(f => f.Bvid == video.Bvid);
            if (existing is not null)
            {
                Favorites.Remove(existing);
                System.Diagnostics.Debug.WriteLine($"[LibraryVM] Removed from favorites: {video.Title}");
            }
            else
            {
                Favorites.Add(video);
                System.Diagnostics.Debug.WriteLine($"[LibraryVM] Added to favorites: {video.Title}");
            }

            IsEmpty = Favorites.Count == 0;
            await SaveFavoritesAsync();
        }

        /// <summary>
        /// 按指定方式排序。
        /// </summary>
        [RelayCommand]
        private async Task SortAsync(string? option)
        {
            if (string.IsNullOrWhiteSpace(option))
            {
                return;
            }

            SortOption = option;

            var list = Favorites.ToList();
            if (option == "标题")
            {
                list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            }
            // "收藏时间"：保持原顺序（最新添加的在末尾）

            Favorites.Clear();
            foreach (var item in list)
            {
                Favorites.Add(item);
            }

            await SaveFavoritesAsync();
        }

        /// <summary>
        /// 从收藏列表中播放指定视频，将整个收藏列表设为播放队列。
        /// </summary>
        [RelayCommand]
        private async Task PlayFromFavoriteAsync(VideoModel? video)
        {
            if (video is null)
            {
                return;
            }

            var index = Favorites.IndexOf(video);
            if (index >= 0)
            {
                await _playerViewModel.SetPlaylistAsync(Favorites.ToList(), index);
            }
        }

        /// <summary>
        /// 从收藏列表中移除指定视频。
        /// </summary>
        [RelayCommand]
        private async Task RemoveFavoriteAsync(VideoModel? video)
        {
            if (video is null)
            {
                return;
            }

            Favorites.Remove(video);
            IsEmpty = Favorites.Count == 0;
            await SaveFavoritesAsync();
            System.Diagnostics.Debug.WriteLine($"[LibraryVM] Removed from favorites: {video.Title}");
        }

        // ── 持久化（L2） ──

        /// <summary>
        /// 从 LocalFolder/favorites.json 加载收藏列表。
        /// </summary>
        private async Task LoadFavoritesAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(FavoritesFileName);
                if (file is StorageFile sf)
                {
                    var json = await FileIO.ReadTextAsync(sf);
                    var items = JsonSerializer.Deserialize<List<VideoModel>>(json);
                    if (items is not null)
                    {
                        Favorites.Clear();
                        foreach (var item in items)
                        {
                            Favorites.Add(item);
                        }
                        System.Diagnostics.Debug.WriteLine(
                            $"[LibraryVM] Loaded {items.Count} favorites from {FavoritesFileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[LibraryVM] Load favorites failed: {ex.Message}");
            }

            IsEmpty = Favorites.Count == 0;
        }

        /// <summary>
        /// 将收藏列表保存到 LocalFolder/favorites.json。
        /// </summary>
        private async Task SaveFavoritesAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(Favorites.ToList(), new JsonSerializerOptions
                {
                    WriteIndented = true,
                });
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    FavoritesFileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[LibraryVM] Save favorites failed: {ex.Message}");
            }
        }

        // ── L4: 自动下载已移除 —— 下载推迟到 PlayerViewModel.PlayVideoAsync 播放时执行 ──
    }
}
