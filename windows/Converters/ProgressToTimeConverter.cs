using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace bilibili_music_player_windows.Converters
{
    /// <summary>
    /// 将 Slider 的归一化进度值（0.0~1.0）转换为 mm:ss 格式的 ToolTip 文本。
    /// 本身是 DependencyObject，通过 TotalDuration 依赖属性接收外部绑定的总时长。
    /// </summary>
    public class ProgressToTimeConverter : DependencyObject, IValueConverter
    {
        public static readonly DependencyProperty TotalDurationProperty =
            DependencyProperty.Register(
                nameof(TotalDuration),
                typeof(TimeSpan),
                typeof(ProgressToTimeConverter),
                new PropertyMetadata(TimeSpan.Zero));

        public TimeSpan TotalDuration
        {
            get => (TimeSpan)GetValue(TotalDurationProperty);
            set => SetValue(TotalDurationProperty, value);
        }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double progress && TotalDuration > TimeSpan.Zero)
            {
                var targetTicks = (long)(TotalDuration.Ticks * Math.Clamp(progress, 0.0, 1.0));
                var currentTime = TimeSpan.FromTicks(targetTicks);

                return currentTime.TotalHours >= 1.0
                    ? currentTime.ToString(@"h\:mm\:ss")
                    : currentTime.ToString(@"mm\:ss");
            }

            return "00:00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
