using Microsoft.UI.Xaml;
using System;

namespace bilibili_music_player_windows
{
    /// <summary>
    /// 供 XAML {x:Bind} 使用的静态辅助方法。
    /// 必须放在独立类中（Window 不是 FrameworkElement，编译器无法处理其上的静态方法）。
    /// </summary>
    public static class ViewHelpers
    {
        /// <summary>格式化 TimeSpan 为 mm:ss 或 h:mm:ss。</summary>
        public static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts <= TimeSpan.Zero) return "--:--";
            return ts.Hours > 0
                ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        public static Visibility BoolToVisibility(bool value) =>
            value ? Visibility.Visible : Visibility.Collapsed;

        public static Visibility InvertBoolToVisibility(bool value) =>
            value ? Visibility.Collapsed : Visibility.Visible;
    }
}