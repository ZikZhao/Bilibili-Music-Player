using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.ViewModels;

namespace bilibili_music_player_windows.Pages;

public sealed partial class PlaylistPage : Page
{
    public PlayerViewModel ViewModel { get; }
    public PlaylistPage()
    {
        ViewModel = App.Current.GetService<PlayerViewModel>();
        InitializeComponent();
    }
    private async void OnQueueItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaybackQueueEntry entry) await ViewModel.PlayQueueEntryAsync(entry);
    }
    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlaybackQueueEntry entry })
            await ViewModel.RemoveQueueEntryAsync(entry);
    }
}
