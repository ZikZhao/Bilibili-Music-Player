using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace bilibili_music_player_windows.Controls;

public class Icon : FontIcon
{
    public Icon()
    {
        FontFamily = (FontFamily)Application.Current.Resources["IconParkFontFamily"];
        FontSize = 20;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        // If consumer explicitly set Foreground, leave it alone.
        // Otherwise, default to Slate800 and track theme changes.
        if (ReadLocalValue(ForegroundProperty) == DependencyProperty.UnsetValue)
        {
            ActualThemeChanged += (_, _) => ApplyThemeForeground();
            ApplyThemeForeground();
        }
    }

    private void ApplyThemeForeground()
    {
        if (Application.Current.Resources.TryGetValue("Slate800", out var value) && value is Brush brush)
            Foreground = brush;
    }
}
