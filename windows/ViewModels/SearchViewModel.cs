#pragma warning disable MVVMTK0045

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bilibili_music_player_windows.ViewModels
{
    public partial class SearchViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<string> _suggestions = new();

        [ObservableProperty]
        private ObservableCollection<string> _history = new();

        [ObservableProperty]
        private ObservableCollection<string> _hotKeywords = new();

        [ObservableProperty]
        private bool _isSearching;

        [ObservableProperty]
        private string? _errorMessage;

        [ObservableProperty]
        private bool _hasMore;

        [ObservableProperty]
        private int _currentPage = 1;

        [RelayCommand]
        private void Search() { }

        [RelayCommand]
        private void LoadMore() { }

        [RelayCommand]
        private void ClearHistory() { }

        [RelayCommand]
        private void Refresh() { }
    }
}
