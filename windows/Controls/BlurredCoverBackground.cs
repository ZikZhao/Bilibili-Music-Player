using System;
using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace bilibili_music_player_windows.Controls;

/// <summary>A bounded cover texture blurred by the compositor, never the foreground UI.</summary>
public sealed class BlurredCoverBackground : Grid, IDisposable
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(BlurredCoverBackground),
        new PropertyMetadata(null, (owner, _) => ((BlurredCoverBackground)owner).UpdateCover()));
    public string? Source { get => (string?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    private const float BlurRadius = 40;
    private SpriteVisual? _visual;
    private CompositionSurfaceBrush? _imageBrush;
    private CompositionEffectBrush? _effectBrush;
    private CompositionEffectFactory? _effectFactory;
    private LoadedImageSurface? _surface;
    private string? _loadedSource, _error;
    private bool _active, _ready, _disposed;
    private int _loads;

    public BlurredCoverBackground()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { CreateVisual(); UpdateCover(); };
        Unloaded += (_, _) => ReleaseVisual();
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (active) { CreateVisual(); UpdateCover(); }
    }

    private void CreateVisual()
    {
        if (_disposed || !IsLoaded || _visual is not null) return;
        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
            _imageBrush = compositor.CreateSurfaceBrush();
            _imageBrush.Stretch = CompositionStretch.UniformToFill;
            using var blur = new GaussianBlurEffect
            {
                BlurAmount = BlurRadius, BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter("Cover"),
            };
            _effectFactory = compositor.CreateEffectFactory(blur);
            _effectBrush = _effectFactory.CreateBrush();
            _effectBrush.SetSourceParameter("Cover", _imageBrush);
            _visual = compositor.CreateSpriteVisual();
            _visual.RelativeSizeAdjustment = Vector2.One;
            _visual.Brush = _effectBrush;
            ElementCompositionPreview.SetElementChildVisual(this, _visual);
        }
        catch (Exception ex)
        {
            // Keep the page's solid fallback if effects are unavailable.
            _error = ex.Message;
            ReleaseVisual();
            System.Diagnostics.Debug.WriteLine($"[Player background] {ex.Message}");
        }
    }

    private void UpdateCover()
    {
        if (_disposed || !_active || _imageBrush is null) return;
        if (Source == _loadedSource && (_surface is not null || string.IsNullOrWhiteSpace(Source))) return;
        ReleaseSurface();
        _loadedSource = Source;
        _error = null;
        if (string.IsNullOrWhiteSpace(Source)) return;
        var normalized = Source.StartsWith("//", StringComparison.Ordinal) ? "https:" + Source : Source;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)) return;
        try
        {
            // Large original Bilibili covers can be several thousand pixels.
            // A 640px texture is sufficient for a heavily blurred full-window image.
            _surface = LoadedImageSurface.StartLoadFromUri(uri, new Size(640, 640));
            _surface.LoadCompleted += OnLoadCompleted;
            _imageBrush.Surface = _surface;
            _loads++;
        }
        catch (Exception ex) { _error = ex.Message; ReleaseSurface(); }
    }

    private void OnLoadCompleted(LoadedImageSurface sender, LoadedImageSourceLoadCompletedEventArgs args)
    {
        if (!ReferenceEquals(sender, _surface) || _disposed) return;
        _ready = args.Status == LoadedImageSourceLoadStatus.Success;
        if (!_ready) { _error = args.Status.ToString(); ReleaseSurface(); }
    }

    private void ReleaseSurface()
    {
        _ready = false;
        if (_imageBrush is not null) _imageBrush.Surface = null;
        if (_surface is null) return;
        _surface.LoadCompleted -= OnLoadCompleted;
        _surface.Dispose();
        _surface = null;
    }

    private void ReleaseVisual()
    {
        ReleaseSurface();
        ElementCompositionPreview.SetElementChildVisual(this, null);
        _visual?.Dispose(); _visual = null;
        _effectBrush?.Dispose(); _effectBrush = null;
        _effectFactory?.Dispose(); _effectFactory = null;
        _imageBrush?.Dispose(); _imageBrush = null;
        _loadedSource = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseVisual();
    }

#if DEBUG
    internal object Inspect() => new { active = _active, ready = _ready, loads = _loads,
        blurRadius = BlurRadius, effects = _effectBrush is not null, error = _error,
        width = _surface?.DecodedSize.Width, height = _surface?.DecodedSize.Height };
#endif
}
