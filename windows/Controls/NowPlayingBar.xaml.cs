using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using bilibili_music_player_windows.ViewModels;

namespace bilibili_music_player_windows.Controls;

public sealed partial class NowPlayingBar : UserControl
{
    public PlayerViewModel ViewModel { get; }
    private PlaybackSeekBinding? _seekBinding;
    public event RoutedEventHandler? DetailsRequested;
    public NowPlayingBar()
    {
        ViewModel = App.Current.GetService<PlayerViewModel>();
        InitializeComponent();
    }
    private void OnNowPlayingClick(object sender, RoutedEventArgs e) => DetailsRequested?.Invoke(this, e);
    private void OnProgressSliderLoaded(object sender, RoutedEventArgs e)
    {
        _seekBinding?.Dispose();
        _seekBinding = new PlaybackSeekBinding(ProgressSlider, ViewModel);
    }
}
