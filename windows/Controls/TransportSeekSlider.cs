using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace bilibili_music_player_windows.Controls;

/// <summary>The native seek slider, with its thumb revealed only during interaction.</summary>
public sealed class TransportSeekSlider : Slider
{
    private Thumb? _thumb;
    private Rectangle? _nativeFill, _centeredFill;
    private FrameworkElement? _container;
    private RectangleGeometry? _viewportClip;
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
        SizeChanged += OnGeometrySizeChanged;
        Unloaded += (_, _) => { _pointerOver = _pressed = false; UpdateThumb(); };
    }

    protected override void OnApplyTemplate()
    {
        if (_thumb is not null) _thumb.SizeChanged -= OnGeometrySizeChanged;
        if (_container is not null)
        {
            _container.SizeChanged -= OnGeometrySizeChanged;
            _container.Margin = default;
            _container.Clip = null;
        }
        if (_centeredFill?.Parent is Panel oldParent) oldParent.Children.Remove(_centeredFill);
        if (_nativeFill is not null) _nativeFill.Opacity = 1;
        _centeredFill = null;
        base.OnApplyTemplate();
        _thumb = GetTemplateChild("HorizontalThumb") as Thumb;
        _nativeFill = GetTemplateChild("HorizontalDecreaseRect") as Rectangle;
        _container = GetTemplateChild("SliderContainer") as FrameworkElement;
        if (_thumb is not null && _container is not null && _nativeFill?.Parent is Grid track)
        {
            // The native rectangle's explicit width also positions the thumb.
            // Keep it as the layout spacer and paint separately, extending into
            // half of the thumb column to end at its center.
            _centeredFill = new Rectangle { Name = "CenteredProgressFill", IsHitTestVisible = false };
            _centeredFill.SetBinding(Shape.FillProperty, FollowFill(nameof(Shape.Fill)));
            _centeredFill.SetBinding(Rectangle.RadiusXProperty, FollowFill(nameof(Rectangle.RadiusX)));
            _centeredFill.SetBinding(Rectangle.RadiusYProperty, FollowFill(nameof(Rectangle.RadiusY)));
            Grid.SetRow(_centeredFill, Grid.GetRow(_nativeFill));
            Grid.SetColumn(_centeredFill, Grid.GetColumn(_nativeFill));
            track.Children.Insert(track.Children.IndexOf(_nativeFill) + 1, _centeredFill);
            _nativeFill.Opacity = 0;
            _viewportClip = new RectangleGeometry();
            _container.Clip = _viewportClip;
            _thumb.SizeChanged += OnGeometrySizeChanged;
            _container.SizeChanged += OnGeometrySizeChanged;
        }
        UpdateGeometry();
        UpdateThumb();
    }

    private Binding FollowFill(string property) => new()
    { Source = _nativeFill, Path = new PropertyPath(property), Mode = BindingMode.OneWay };

    private void OnGeometrySizeChanged(object sender, SizeChangedEventArgs e) => UpdateGeometry();

    private void UpdateGeometry()
    {
        if (_centeredFill is null || _thumb is null || _container is null || _viewportClip is null) return;
        var half = _thumb.ActualWidth / 2;
        // Native Slider subtracts one thumb width from its container's travel.
        // Extend that container equally at both ends, then clip it to the real
        // viewport: the thumb center now travels exactly from 0% to 100%.
        _container.Margin = new Thickness(-half, 0, -half, 0);
        _centeredFill.Margin = new Thickness(0, 0, -half, 0);
        _viewportClip.Rect = new Rect(half, 0,
            Math.Max(0, ActualWidth - Padding.Left - Padding.Right), _container.ActualHeight);
    }

    private void EndPress(object sender, PointerRoutedEventArgs e) { _pressed = false; UpdateThumb(); }

    private void UpdateThumb()
    {
        // Opacity preserves the native hit target, track geometry and automation.
        if (_thumb is not null)
            _thumb.Opacity = IsEnabled && (_pointerOver || _pressed || FocusState == FocusState.Keyboard) ? 1 : 0;
    }
}
