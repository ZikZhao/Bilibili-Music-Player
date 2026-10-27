using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace bilibili_music_player_windows.Converters;

/// <summary>Keep normalized playback volume internally; show a whole number in slider tooltips.</summary>
public sealed class VolumePercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is double volume ? Format(volume) : "0";

    internal static string Format(double volume) =>
        double.IsFinite(volume)
            ? Math.Round(Math.Clamp(volume, 0, 1) * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
            : "0";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
