using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace bilibili_music_player_windows.Controls;

/// <summary>Wraps content at its natural width instead of allocating equal-size cells.</summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(8d, (owner, _) => ((WrapPanel)owner).InvalidateMeasure()));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return Layout(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, arrange: true);
        return finalSize;
    }

    private Size Layout(double availableWidth, bool arrange)
    {
        double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
        var spacing = Math.Max(0, Spacing);
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > availableWidth)
            {
                x = 0;
                y += rowHeight + spacing;
                rowHeight = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, size.Width, size.Height));
            usedWidth = Math.Max(usedWidth, x + size.Width);
            rowHeight = Math.Max(rowHeight, size.Height);
            x += size.Width + spacing;
        }
        return new Size(usedWidth, y + rowHeight);
    }
}
