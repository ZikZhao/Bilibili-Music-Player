using System;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace bilibili_music_player_windows.Controls;

/// <summary>One shared 16:9 frame for every Bilibili video thumbnail.</summary>
public sealed class AspectRatioPanel : Panel
{
    public const double Ratio = 16d / 9;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width :
            double.IsFinite(availableSize.Height) ? availableSize.Height * Ratio : 0;
        var size = new Size(width, width / Ratio);
        foreach (var child in Children) child.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children) child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}
