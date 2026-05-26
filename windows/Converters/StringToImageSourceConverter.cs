using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace bilibili_music_player_windows.Converters
{
    /// <summary>
    /// Converts a string URL to an <see cref="ImageSource"/> for use with <see cref="Microsoft.UI.Xaml.Controls.Image"/> binding.
    /// If the string is null or empty, returns null (no image).
    /// </summary>
    public sealed class StringToImageSourceConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, string language)
        {
            if (value is string url && !string.IsNullOrWhiteSpace(url))
            {
                // Handle protocol-relative URLs (starting with //)
                if (url.StartsWith("//", StringComparison.Ordinal))
                {
                    url = $"https:{url}";
                }

                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    return new BitmapImage(uri);
                }
            }

            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}
