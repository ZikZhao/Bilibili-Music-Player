#if DEBUG
using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace bilibili_music_player_windows.Controls;

public sealed partial class NowPlayingBar
{
    private readonly List<(FrameworkElement Target, Rectangle Frame, Line Center)> _typographyBounds = new();
    private Canvas? _typographyCanvas;

    // Temporary typography comparison. This overlay never participates in
    // measuring the text/icons, adds no padding and cannot intercept input.
    private void InitializeTypographyBounds()
    {
        _typographyCanvas = new Canvas { Name = "TypographyBoundsOverlay", IsHitTestVisible = false };
        Canvas.SetZIndex(_typographyCanvas, 100);
        TransportRoot.Children.Add(_typographyCanvas);
        AddTypographyBounds(TransportTime, ColorHelper.FromArgb(255, 0, 220, 150));
        AddTypographyBounds(CompactTransportTime, ColorHelper.FromArgb(255, 0, 220, 150));
        foreach (var icon in new FrameworkElement[] { PreviousTransportIcon, PlayTransportIcon, PauseTransportIcon,
                     NextTransportIcon, AudibleTransportIcon, MutedTransportIcon })
            AddTypographyBounds(icon, ColorHelper.FromArgb(255, 255, 160, 40));
        Loaded += (_, _) => { LayoutUpdated += UpdateTypographyBounds; UpdateTypographyBounds(this, null!); };
        Unloaded += (_, _) => LayoutUpdated -= UpdateTypographyBounds;
    }

    private void AddTypographyBounds(FrameworkElement target, Windows.UI.Color color)
    {
        var brush = new SolidColorBrush(color);
        var frame = new Rectangle { Name = target.Name + "LogicalFrame", Stroke = brush, StrokeThickness = 1 };
        var center = new Line { Stroke = brush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 2 } };
        _typographyCanvas!.Children.Add(frame);
        _typographyCanvas.Children.Add(center);
        _typographyBounds.Add((target, frame, center));
    }

    private void UpdateTypographyBounds(object? sender, object e)
    {
        if (_typographyCanvas is null) return;
        foreach (var (target, frame, center) in _typographyBounds)
        {
            var visible = target.Visibility == Visibility.Visible && target.ActualWidth > 0 && target.ActualHeight > 0;
            var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (frame.Visibility != visibility) frame.Visibility = visibility;
            if (center.Visibility != visibility) center.Visibility = visibility;
            if (!visible) continue;
            var bounds = target.TransformToVisual(_typographyCanvas)
                .TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            if (Canvas.GetLeft(frame) == bounds.X && Canvas.GetTop(frame) == bounds.Y &&
                frame.Width == bounds.Width && frame.Height == bounds.Height) continue;
            Canvas.SetLeft(frame, bounds.X);
            Canvas.SetTop(frame, bounds.Y);
            frame.Width = bounds.Width;
            frame.Height = bounds.Height;
            center.X1 = bounds.X;
            center.X2 = bounds.X + bounds.Width;
            center.Y1 = center.Y2 = bounds.Y + bounds.Height / 2;
        }
    }
}
#endif
