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
    }
}
