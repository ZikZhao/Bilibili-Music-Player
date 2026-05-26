#pragma warning disable MVVMTK0045

using System.Collections.ObjectModel;
using bilibili_music_player_windows.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bilibili_music_player_windows.ViewModels
{
    public partial class LibraryViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<VideoPreviewItem> _favorites = new();

        [ObservableProperty]
        private string _sortOption = "date";

        [ObservableProperty]
        private ObservableCollection<string> _sortOptions = new() { "date", "title" };

        [ObservableProperty]
        private bool _isEmpty = true;

        [RelayCommand]
        private void ToggleFavorite(VideoPreviewItem? video) { }

        [RelayCommand]
        private void Sort(string option) { }

        [RelayCommand]
        private void PlayFromFavorite(VideoPreviewItem? video) { }

        [RelayCommand]
        private void RemoveFavorite(VideoPreviewItem? video) { }
    }
}
