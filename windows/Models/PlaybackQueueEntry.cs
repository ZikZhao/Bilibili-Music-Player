#pragma warning disable MVVMTK0045
using CommunityToolkit.Mvvm.ComponentModel;

namespace bilibili_music_player_windows.Models;

/// <summary>A distinct queue occurrence, even when the same video is added twice.</summary>
public sealed partial class PlaybackQueueEntry : ObservableObject
{
    [ObservableProperty] private VideoModel _track;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isDimmed;
    [ObservableProperty] private int _order;

    public PlaybackQueueEntry(VideoModel track) => _track = track;
    public override string ToString() => Track.Title;
    public string SequenceText => IsCurrent ? string.Empty : Order.ToString();
    public double RowOpacity => IsDimmed ? .4 : 1;
    public double IndicatorOpacity => IsPlaying ? 1 : .5;
    public string DurationText => string.IsNullOrEmpty(Track.Duration)
        ? ViewModels.PlayerViewModel.FormatTime(System.TimeSpan.FromSeconds(Track.DurationSeconds)) : Track.Duration;

    internal void Update(int order, bool current, bool playing, bool dimmed)
    { Order = order; IsCurrent = current; IsPlaying = playing; IsDimmed = dimmed; }
    partial void OnIsCurrentChanged(bool value) => OnPropertyChanged(nameof(SequenceText));
    partial void OnOrderChanged(int value) => OnPropertyChanged(nameof(SequenceText));
    partial void OnIsDimmedChanged(bool value) => OnPropertyChanged(nameof(RowOpacity));
    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(IndicatorOpacity));
    partial void OnTrackChanged(VideoModel value) => OnPropertyChanged(nameof(DurationText));
}
