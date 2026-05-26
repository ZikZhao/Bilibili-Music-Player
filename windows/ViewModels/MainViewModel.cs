#pragma warning disable MVVMTK0045

using System.Collections.ObjectModel;
using bilibili_music_player_windows.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace bilibili_music_player_windows.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<TrackItem> _favorites = new();
    }
}
