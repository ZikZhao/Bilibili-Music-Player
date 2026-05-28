using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace bilibili_music_player_windows.Controls
{
    public sealed partial class SvgIcon : UserControl
    {
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(string), typeof(SvgIcon), new PropertyMetadata(null, OnDataChanged));

        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(SvgIcon), new PropertyMetadata(new SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 51, 51))));

        public static readonly DependencyProperty StrokeWidthProperty =
            DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(SvgIcon), new PropertyMetadata(4.0));

        public static readonly DependencyProperty FillProperty =
            DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(SvgIcon), new PropertyMetadata(new SolidColorBrush(Microsoft.UI.Colors.Transparent)));

        public string Data
        {
            get => (string)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public double StrokeWidth
        {
            get => (double)GetValue(StrokeWidthProperty);
            set => SetValue(StrokeWidthProperty, value);
        }

        public Brush Fill
        {
            get => (Brush)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public SvgIcon()
        {
            this.InitializeComponent();
        }

        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SvgIcon icon && e.NewValue is string pathData && !string.IsNullOrWhiteSpace(pathData))
            {
                icon.IconPath.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), pathData);
            }
            else if (d is SvgIcon emptyIcon)
            {
                emptyIcon.IconPath.Data = null;
            }
        }
    }
}