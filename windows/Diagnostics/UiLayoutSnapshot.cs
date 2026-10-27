#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace bilibili_music_player_windows.Diagnostics;

internal static class UiLayoutSnapshot
{
    public static FrameworkElement? Find(DependencyObject node, string name)
    {
        if (node is FrameworkElement element && (element.Name == name || AutomationProperties.GetAutomationId(element) == name))
            return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (Find(VisualTreeHelper.GetChild(node, i), name) is { } found) return found;
        return null;
    }

    public static string? CommonState(Control control) =>
        VisualTreeHelper.GetChildrenCount(control) > 0 && VisualTreeHelper.GetChild(control, 0) is FrameworkElement template
            ? VisualStateManager.GetVisualStateGroups(template).FirstOrDefault(g => g.Name == "CommonStates")?.CurrentState?.Name
            : null;

    public static object Capture(FrameworkElement root)
    {
        var nodes = new List<Dictionary<string, object?>>();
        Visit(root, root, null, true, nodes);
        return new
        {
            units = "DIP", width = root.ActualWidth, height = root.ActualHeight,
            scale = root.XamlRoot?.RasterizationScale ?? 1, theme = root.ActualTheme.ToString(),
            note = "Actual XAML layout, not a lossless vector rendering. TextBlock baseline is measured; TextBox glyph baselines are not inferred.",
            nodes,
        };
    }

    private static void Visit(DependencyObject node, FrameworkElement root, int? parent, bool visible,
        List<Dictionary<string, object?>> output)
    {
        if (node is FrameworkElement element)
        {
            // Authentication input must never appear in diagnostic JSON exports.
            if (element is bilibili_music_player_windows.Controls.AccountDialog) return;
            visible &= element.Visibility == Visibility.Visible;
            if (!visible) return; // Hidden resident pages retain stale geometry.
            Rect bounds;
            try { bounds = element.TransformToVisual(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
            catch { return; }
            var id = output.Count;
            var data = new Dictionary<string, object?>
            {
                ["id"] = id, ["parent"] = parent, ["type"] = element.GetType().Name,
                ["name"] = element.Name, ["automationId"] = AutomationProperties.GetAutomationId(element),
                ["bounds"] = Box(bounds), ["layoutSlot"] = Box(LayoutInformation.GetLayoutSlot(element)),
                ["desiredWidth"] = element.DesiredSize.Width, ["desiredHeight"] = element.DesiredSize.Height,
                ["margin"] = Insets(element.Margin), ["opacity"] = element.Opacity,
                ["clip"] = element.Clip is RectangleGeometry clip ? Box(clip.Rect) : null,
                ["horizontalAlignment"] = element.HorizontalAlignment.ToString(),
                ["verticalAlignment"] = element.VerticalAlignment.ToString(),
                ["row"] = Grid.GetRow(element), ["column"] = Grid.GetColumn(element),
                ["rowSpan"] = Grid.GetRowSpan(element), ["columnSpan"] = Grid.GetColumnSpan(element),
            };
            if (element is Control control)
            {
                data["padding"] = Insets(control.Padding);
                data["fontFamily"] = control.FontFamily.Source;
                data["fontSize"] = control.FontSize;
                data["foreground"] = Paint(control.Foreground);
                data["background"] = Paint(control.Background);
                data["borderBrush"] = Paint(control.BorderBrush);
                data["borderThickness"] = Insets(control.BorderThickness);
                data["focusState"] = control.FocusState.ToString();
                data["isEnabled"] = control.IsEnabled;
            }
            if (element is RangeBase range) data["value"] = range.Value;
            if (element is Slider { ThumbToolTipValueConverter: { } converter } slider)
                data["thumbToolTipText"] = converter.Convert(slider.Value, typeof(string), null!, "");
            if (element is TextBlock text)
            {
                data["text"] = text.Text; data["fontFamily"] = text.FontFamily.Source;
                data["fontSize"] = text.FontSize; data["fontWeight"] = text.FontWeight.Weight;
                data["foreground"] = Paint(text.Foreground); data["padding"] = Insets(text.Padding);
                data["baselineOffset"] = text.BaselineOffset; data["baselineY"] = bounds.Y + text.BaselineOffset;
            }
            if (element is TextBox input)
            {
                data["text"] = input.Text; data["placeholderText"] = input.PlaceholderText;
                data["firstCharacterRect"] = input.Text.Length > 0 ? Box(input.GetRectFromCharacterIndex(0, false)) : null;
                data["selectionStart"] = input.SelectionStart;
                data["selectionLength"] = input.SelectionLength;
            }
            if (element is Border border)
            {
                data["background"] = Paint(border.Background); data["borderBrush"] = Paint(border.BorderBrush);
                data["borderThickness"] = Insets(border.BorderThickness); data["padding"] = Insets(border.Padding);
            }
            if (element is ContentPresenter presenter)
            {
                data["background"] = Paint(presenter.Background); data["borderBrush"] = Paint(presenter.BorderBrush);
                data["borderThickness"] = Insets(presenter.BorderThickness); data["padding"] = Insets(presenter.Padding);
            }
            if (element is Panel panel) data["background"] = Paint(panel.Background);
            if (element is Grid grid)
            {
                data["columns"] = grid.ColumnDefinitions.Select(c => new { width = c.Width.ToString(), actualWidth = c.ActualWidth }).ToArray();
                data["rows"] = grid.RowDefinitions.Select(r => new { height = r.Height.ToString(), actualHeight = r.ActualHeight }).ToArray();
            }
            if (element is bilibili_music_player_windows.Controls.SearchTextBox search) data["templateStatus"] = search.TemplateStatus;
            if (element is Shape shape) { data["background"] = Paint(shape.Fill); data["borderBrush"] = Paint(shape.Stroke); }
            if (element is FontIcon icon)
            {
                data["glyph"] = icon.Glyph; data["fontFamily"] = icon.FontFamily.Source;
                data["fontSize"] = icon.FontSize; data["foreground"] = Paint(icon.Foreground);
            }
            if (element is ScrollViewer scroll)
            {
                data["horizontalOffset"] = scroll.HorizontalOffset; data["verticalOffset"] = scroll.VerticalOffset;
            }
            var groups = VisualStateManager.GetVisualStateGroups(element);
            if (groups.Count > 0) data["states"] = groups.ToDictionary(g => g.Name, g => g.CurrentState?.Name);
            output.Add(data);
            parent = id;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            Visit(VisualTreeHelper.GetChild(node, i), root, parent, visible, output);
    }

    private static object Box(Rect rect) => new { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height };
    private static object Insets(Thickness value) => new { left = value.Left, top = value.Top, right = value.Right, bottom = value.Bottom };
    private static object? Paint(Brush? brush) => brush switch
    {
        null => null,
        SolidColorBrush solid => new { kind = "solid", color = $"#{solid.Color.A:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}", opacity = solid.Opacity },
        LinearGradientBrush gradient => (object)new { kind = "linearGradient", opacity = gradient.Opacity,
            stops = gradient.GradientStops.Select(s => new { offset = s.Offset, color = $"#{s.Color.A:X2}{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" }).ToArray() },
        _ => (object)new { kind = brush.GetType().Name, opacity = brush.Opacity },
    };
}
#endif
