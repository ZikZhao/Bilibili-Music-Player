using System.Diagnostics;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace bilibili_music_player_windows.Controls;

/// <summary>A native TextBox with a search button before its editing column.</summary>
public sealed class SearchTextBox : TextBox
{
    private Button? _searchButton;
    internal string TemplateStatus { get; private set; } = "Not applied";

    public event RoutedEventHandler? SearchRequested;

    public SearchTextBox() => DefaultStyleKey = typeof(TextBox);

    /// <summary>The native editing surface's left edge, including its prefix and padding.</summary>
    internal double TextStartOffset
    {
        get
        {
            if (GetTemplateChild("ContentElement") is not FrameworkElement content) return 0;
            // Use the viewport inset, so scrolling a long query never shifts
            // the candidate list's text origin.
            var inset = content is Control control ? control.Padding.Left : Padding.Left;
            return content.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).X + inset;
        }
    }

    protected override void OnApplyTemplate()
    {
        if (_searchButton is not null) _searchButton.Click -= OnSearchClick;
        base.OnApplyTemplate();

        // Keep the SDK's text presenter, placeholder, clear button, bindings and
        // visual states intact. Native editor/caret layout and clipping stay
        // unchanged; the placeholder-only optical inset is applied below.
        if (GetTemplateChild("ContentElement") is not FrameworkElement content ||
            GetTemplateChild("PlaceholderTextContentPresenter") is not FrameworkElement placeholder ||
            GetTemplateChild("DeleteButton") is not FrameworkElement deleteButton ||
            GetTemplateChild("BorderElement") is not FrameworkElement border ||
            content.Parent is not Grid grid || placeholder.Parent != grid ||
            deleteButton.Parent != grid || border.Parent != grid)
        {
            TemplateStatus = "Missing native part or unexpected parent: " + string.Join(", ",
                new[] { "ContentElement", "PlaceholderTextContentPresenter", "DeleteButton", "BorderElement" }
                    .Select(name => $"{name}={GetTemplateChild(name)?.GetType().Name ?? "null"}"));
            Debug.WriteLine("[SearchTextBox] " + TemplateStatus);
            return;
        }

        // Applying the same template twice must not add a second prefix column.
        if (_searchButton?.Parent == grid)
        {
            TemplateStatus = "Reused prefix";
            _searchButton.Click += OnSearchClick;
            return;
        }
        _searchButton = null;
        var inputColumn = Grid.GetColumn(content);
        if (grid.ColumnDefinitions.Count < inputColumn + 2 ||
            Grid.GetRow(placeholder) != Grid.GetRow(content) || Grid.GetRow(deleteButton) != Grid.GetRow(content) ||
            Grid.GetRow(border) != Grid.GetRow(content) ||
            Grid.GetColumn(placeholder) != inputColumn || Grid.GetColumnSpan(placeholder) != 2 ||
            Grid.GetColumn(deleteButton) != inputColumn + 1 ||
            Grid.GetColumn(border) != inputColumn || Grid.GetColumnSpan(border) != 2)
        {
            TemplateStatus = $"Unsupported columns: {grid.ColumnDefinitions.Count}; content={Grid.GetColumn(content)}; placeholder={Grid.GetColumn(placeholder)}/{Grid.GetColumnSpan(placeholder)}; delete={Grid.GetColumn(deleteButton)}; border={Grid.GetColumn(border)}/{Grid.GetColumnSpan(border)}";
            Debug.WriteLine("[SearchTextBox] " + TemplateStatus);
            return;
        }

        var children = grid.Children.OfType<FrameworkElement>().ToArray();
        // Recent runtimes add validation columns around the editing area. Insert
        // inside its existing chrome rather than assuming it begins at column 0.
        grid.ColumnDefinitions.Insert(inputColumn, new ColumnDefinition { Width = GridLength.Auto });
        foreach (var child in children)
        {
            var column = Grid.GetColumn(child);
            var span = Grid.GetColumnSpan(child);
            if (child != placeholder && child != content && column <= inputColumn &&
                column + span > inputColumn && (column < inputColumn || span > 1))
                Grid.SetColumnSpan(child, span + 1);
            else if (column >= inputColumn)
                Grid.SetColumn(child, column + 1);
        }

        // TextBlock placeholder metrics differ from the RichEdit editing layer.
        // Move only placeholder text up 1 DIP, preserving its total padding and
        // the control's natural height. Track future native/theme padding changes.
        if (placeholder is TextBlock placeholderText)
            placeholderText.SetBinding(TextBlock.PaddingProperty, new Binding
            {
                Source = this,
                Path = new PropertyPath(nameof(Padding)),
                Converter = new PlaceholderPaddingConverter(),
            });

        _searchButton = new SearchPrefixButton
        {
            Name = "SearchPrefixButton",
            Width = 32,
            MinHeight = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = new FontIcon { Glyph = "\uE721", FontSize = 16 },
        };
        // Keep native keyboard focus and button behavior, with no background or
        // outline in any interaction state (including pointer over and pressed).
        foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed",
            "ButtonBackgroundDisabled", "ButtonBorderBrush", "ButtonBorderBrushPointerOver",
            "ButtonBorderBrushPressed", "ButtonBorderBrushDisabled" })
            _searchButton.Resources[key] = new SolidColorBrush(Colors.Transparent);
        _searchButton.SetBinding(ForegroundProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        AutomationProperties.SetName(_searchButton, "搜索");
        AutomationProperties.SetAutomationId(_searchButton, "SubmitSearchButton");
        ToolTipService.SetToolTip(_searchButton, "搜索 (Enter)");
        Grid.SetRow(_searchButton, Grid.GetRow(content));
        Grid.SetColumn(_searchButton, inputColumn);
        _searchButton.Click += OnSearchClick;
        grid.Children.Add(_searchButton);
        TemplateStatus = "Native prefix attached";
    }

    private void OnSearchClick(object sender, RoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        SearchRequested?.Invoke(this, e);
    }

    private sealed class SearchPrefixButton : Button
    {
        // Override the parent TextBox's I-beam without changing button visuals.
        public SearchPrefixButton() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    private sealed class PlaceholderPaddingConverter : IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, string language)
        {
            if (value is not Thickness padding) return value;
            var offset = System.Math.Min(1, padding.Top);
            return new Thickness(padding.Left, padding.Top - offset, padding.Right, padding.Bottom + offset);
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, string language) =>
            throw new System.NotSupportedException();
    }
}
