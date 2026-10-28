using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace bilibili_music_player_windows.Controls;

/// <summary>A reversible sheet. Read the animated value before stopping the old
/// storyboard so changing direction never snaps to either endpoint.</summary>
internal sealed class PlayerSheetTransition : IDisposable
{
    private readonly FrameworkElement _sheet;
    private readonly FrameworkElement _viewport;
    private readonly Action<bool> _settled;
    private readonly Func<bool> _animationsEnabled;
    private readonly TranslateTransform _translation = new();
    private readonly RectangleGeometry _clip = new();
    private Storyboard? _storyboard;
    private double _height;
    public bool IsOpen { get; private set; }
    public bool IsAnimating => _storyboard is not null;
    public bool MotionEnabled => _animationsEnabled() && new UISettings().AnimationsEnabled;
    public double VisibleFraction => _height > 0 ? 1 - Math.Clamp(_translation.Y / _height, 0, 1) : 0;

    public PlayerSheetTransition(FrameworkElement sheet, FrameworkElement viewport, Func<bool> animationsEnabled, Action<bool> settled)
    {
        _sheet = sheet; _viewport = viewport; _settled = settled; _animationsEnabled = animationsEnabled;
        sheet.RenderTransform = _translation;
        viewport.Clip = _clip;
        viewport.SizeChanged += OnSizeChanged;
        Resize();
    }

    public void SetOpen(bool open)
    {
        var wasHidden = _sheet.Visibility == Visibility.Collapsed;
        var current = wasHidden ? _height : _translation.Y;
        IsOpen = open;
        _sheet.IsHitTestVisible = open;
        if (open) _sheet.Visibility = Visibility.Visible;
        Animate(current);
    }

    private void Animate(double current)
    {
        var previous = _storyboard;
        _storyboard = null;
        previous?.Stop();
        _translation.Y = current;
        var target = IsOpen ? 0 : _height;
        if (_height <= 0 || Math.Abs(target - current) < .5 || !MotionEnabled)
        { Complete(target); return; }

        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = current });
        animation.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(
                Math.Clamp(340 * Math.Abs(target - current) / _height, 80, 340))),
            Value = target,
            KeySpline = new KeySpline { ControlPoint1 = new Point(.16, 1), ControlPoint2 = new Point(.3, 1) },
        });
        Storyboard.SetTarget(animation, _translation);
        Storyboard.SetTargetProperty(animation, nameof(TranslateTransform.Y));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _storyboard = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_storyboard, storyboard)) return;
            _storyboard = null;
            storyboard.Stop();
            Complete(target);
        };
        storyboard.Begin();
    }

    private void Complete(double target)
    {
        _translation.Y = target;
        if (!IsOpen) _sheet.Visibility = Visibility.Collapsed;
        _settled(IsOpen);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Resize();
    private void Resize()
    {
        var fraction = _height > 0 ? _translation.Y / _height : IsOpen ? 0 : 1;
        _height = _viewport.ActualHeight;
        _clip.Rect = new Rect(0, 0, _viewport.ActualWidth, _height);
        if (IsAnimating) Animate(fraction * _height);
        else _translation.Y = IsOpen ? 0 : _height;
    }

    public void Dispose()
    {
        _viewport.SizeChanged -= OnSizeChanged;
        var storyboard = _storyboard; _storyboard = null;
        storyboard?.Stop();
    }
}
