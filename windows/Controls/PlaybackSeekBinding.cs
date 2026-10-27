using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using bilibili_music_player_windows.ViewModels;

namespace bilibili_music_player_windows.Controls;

/// <summary>Native Slider interaction: pointer/keyboard previews, one seek on commit, cancel on source change.</summary>
public sealed class PlaybackSeekBinding : IDisposable
{
    private readonly Slider _slider;
    private readonly PlayerViewModel _player;
    private bool _active;
    public PlaybackSeekBinding(Slider slider, PlayerViewModel player)
    {
        _slider = slider; _player = player;
        var converter = new Converters.ProgressToTimeConverter();
        BindingOperations.SetBinding(converter, Converters.ProgressToTimeConverter.TotalDurationProperty,
            new Binding { Source = player, Path = new PropertyPath(nameof(PlayerViewModel.Duration)), Mode = BindingMode.OneWay });
        slider.ThumbToolTipValueConverter = converter;
        slider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Pressed), true);
        slider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Released), true);
        slider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Released), true);
        slider.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(Cancelled), true);
        slider.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(KeyDown), true);
        slider.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler(KeyUp), true);
        slider.ValueChanged += ValueChanged;
        slider.LostFocus += LostFocus;
        slider.Unloaded += Unloaded;
    }
    private void Begin()
    {
        if (!_player.CanSeek || _active) return;
        _active = true; _player.BeginSeek(); _player.PreviewSeek(_slider.Value);
    }
    private void Commit(bool commit)
    {
        if (!_active) return;
        _active = false; _player.EndSeek(_slider.Value, commit);
    }
    private void Pressed(object sender, PointerRoutedEventArgs e) => Begin();
    private void Released(object sender, PointerRoutedEventArgs e) => Commit(true);
    private void Cancelled(object sender, PointerRoutedEventArgs e) => Commit(false);
    private void ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_active) _player.PreviewSeek(e.NewValue);
        // UI Automation can change Value without pointer/key events. Binding
        // updates already match the model and must never cause another seek.
        else if (_player.CanSeek && Math.Abs(e.NewValue - _player.Progress) > .0000001)
            _player.SeekCommand.Execute(e.NewValue);
    }
    private static bool IsSeekKey(VirtualKey key) => key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down
        or VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown;
    private void KeyDown(object sender, KeyRoutedEventArgs e) { if (IsSeekKey(e.Key)) Begin(); }
    private void KeyUp(object sender, KeyRoutedEventArgs e) { if (IsSeekKey(e.Key)) Commit(true); }
    private void LostFocus(object sender, RoutedEventArgs e) => Commit(true);
    private void Unloaded(object sender, RoutedEventArgs e) => Dispose();
    public void Dispose()
    {
        Commit(false);
        _slider.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Pressed));
        _slider.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Released));
        _slider.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Released));
        _slider.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(Cancelled));
        _slider.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(KeyDown));
        _slider.RemoveHandler(UIElement.KeyUpEvent, new KeyEventHandler(KeyUp));
        _slider.ValueChanged -= ValueChanged; _slider.LostFocus -= LostFocus; _slider.Unloaded -= Unloaded;
    }
}
