using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace bilibili_music_player_windows.Controls;

/// <summary>The native seek slider, with its thumb revealed only during interaction.</summary>
public sealed class TransportSeekSlider : Slider
{
    private Thumb? _thumb;
    private bool _pointerOver, _pressed;

    public TransportSeekSlider()
    {
        DefaultStyleKey = typeof(Slider);
        PointerEntered += (_, _) => { _pointerOver = true; UpdateThumb(); };
        PointerExited += (_, _) => { _pointerOver = false; UpdateThumb(); };
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => { _pressed = true; UpdateThumb(); }), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(EndPress), true);
        AddHandler(PointerCaptureLostEvent, new PointerEventHandler(EndPress), true);
        AddHandler(PointerCanceledEvent, new PointerEventHandler(EndPress), true);
        GotFocus += (_, _) => UpdateThumb();
        LostFocus += (_, _) => UpdateThumb();
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => UpdateThumb());
        Unloaded += (_, _) => { _pointerOver = _pressed = false; UpdateThumb(); };
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _thumb = GetTemplateChild("HorizontalThumb") as Thumb;
        UpdateThumb();
    }

    private void EndPress(object sender, PointerRoutedEventArgs e) { _pressed = false; UpdateThumb(); }

    private void UpdateThumb()
    {
        // Opacity preserves the native hit target, track geometry and automation.
        if (_thumb is not null)
            _thumb.Opacity = IsEnabled && (_pointerOver || _pressed || FocusState == FocusState.Keyboard) ? 1 : 0;
    }
}
