using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Linq;
using bilibili_music_player_windows.ViewModels;

namespace bilibili_music_player_windows.Controls;

public sealed partial class NowPlayingBar : UserControl
{
    public PlayerViewModel ViewModel { get; }
    private PlaybackSeekBinding? _seekBinding;
    private VisualStateGroup? _detailsStates;
    public event RoutedEventHandler? DetailsRequested;
    public NowPlayingBar()
    {
        ViewModel = App.Current.GetService<PlayerViewModel>();
        InitializeComponent();
    }
    private void OnNowPlayingClick(object sender, RoutedEventArgs e) => DetailsRequested?.Invoke(this, e);
    private void OnDetailsLoaded(object sender, RoutedEventArgs e)
        // Native template state groups can still be deferred during Loaded.
        => DispatcherQueue.TryEnqueue(AttachDetailsStates);
    private void AttachDetailsStates()
    {
        if (!NowPlayingDetails.IsLoaded) return;
        if (_detailsStates is not null) _detailsStates.CurrentStateChanged -= OnDetailsStateChanged;
        NowPlayingDetails.ApplyTemplate();
        if (VisualTreeHelper.GetChildrenCount(NowPlayingDetails) > 0 &&
            VisualTreeHelper.GetChild(NowPlayingDetails, 0) is FrameworkElement template)
        {
            _detailsStates = VisualStateManager.GetVisualStateGroups(template).FirstOrDefault(g => g.Name == "CommonStates");
            if (_detailsStates is not null) _detailsStates.CurrentStateChanged += OnDetailsStateChanged;
        }
        UpdateDetailsHover();
    }
    private void OnDetailsUnloaded(object sender, RoutedEventArgs e)
    {
        if (_detailsStates is not null) _detailsStates.CurrentStateChanged -= OnDetailsStateChanged;
        _detailsStates = null;
    }
    private void OnDetailsStateChanged(object sender, VisualStateChangedEventArgs e) => UpdateDetailsHover(e.NewState?.Name);
    private void OnDetailsFocusChanged(object sender, RoutedEventArgs e) => UpdateDetailsHover();
    private void UpdateDetailsHover(string? state = null)
    {
        var active = NowPlayingDetails.IsEnabled && ((state ?? _detailsStates?.CurrentState?.Name) is "PointerOver" or "Pressed" ||
            NowPlayingDetails.FocusState == FocusState.Keyboard);
        CoverActivationLayer.Opacity = active ? 1 : 0;
        CoverLoadingRing.Opacity = active ? 0 : 1;
    }
    internal void ResetDetailsHover()
    {
        VisualStateManager.GoToState(NowPlayingDetails, "Normal", false);
        CoverActivationLayer.Opacity = 0;
        CoverLoadingRing.Opacity = 1;
    }
    private void OnProgressSliderLoaded(object sender, RoutedEventArgs e)
    {
        _seekBinding?.Dispose();
        _seekBinding = new PlaybackSeekBinding(ProgressSlider, ViewModel);
    }
}
