using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace bilibili_music_player_windows.Controls;

public sealed partial class PlaybackQueueRow : UserControl
{
    public event RoutedEventHandler? RemoveRequested;
    public PlaybackQueueRow() => InitializeComponent();
    private void OnRemoveClick(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, e);
}
