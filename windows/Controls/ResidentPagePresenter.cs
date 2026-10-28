using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace bilibili_music_player_windows.Controls;

/// <summary>Keep the previous page painted until a new page has inherited its
/// theme, loaded its templates and completed a hidden layout/render pass.</summary>
internal sealed class ResidentPagePresenter : IDisposable
{
    private readonly FrameworkElement _host;
    private readonly Dictionary<Page, ElementTheme> _prepared = new();
    private int _frames;
    public Page? Current { get; private set; }
    public Page? Pending { get; private set; }
    public int LastPreparationFrames { get; private set; }
    public bool LastPreparationHadGap { get; private set; }
    public bool IsReady(Page page) => _prepared.TryGetValue(page, out var theme) && theme == _host.ActualTheme;

    public ResidentPagePresenter(FrameworkElement host) => _host = host;

    public void Show(Page page)
    {
        CancelPending();
        page.IsHitTestVisible = false;
        if (ReferenceEquals(Current, page) || IsReady(page)) { Reveal(page); return; }
        // Opacity permits Loaded, theme inheritance and layout, unlike Collapsed.
        page.Opacity = 0;
        page.Visibility = Visibility.Visible;
        Pending = page;
        _frames = 0;
        LastPreparationFrames = 0;
        LastPreparationHadGap = false;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Deactivate(Page page)
    {
        page.IsHitTestVisible = false;
        if (ReferenceEquals(Pending, page)) CancelPending();
        // Keep the current page as a paused fallback until Reveal replaces it.
        if (!ReferenceEquals(Current, page)) page.Visibility = Visibility.Collapsed;
    }

    public void Suspend()
    {
        CancelPending();
        if (Current is { } current)
        { current.Visibility = Visibility.Visible; current.IsHitTestVisible = false; }
    }

    private void OnRendering(object? sender, object args)
    {
        if (Pending is not { } page) return;
        if (Current is { } previous && (previous.Visibility != Visibility.Visible || previous.Opacity < 1))
            LastPreparationHadGap = true;
        if (!page.IsLoaded || page.ActualWidth <= 0 || page.ActualHeight <= 0 || page.ActualTheme != _host.ActualTheme)
        { _frames = 0; return; }
        // A Rendering callback precedes composition. Let the first hidden frame
        // complete before revealing on the next, without an arbitrary timer.
        if (++_frames < 2) return;
        CompositionTarget.Rendering -= OnRendering;
        Pending = null;
        LastPreparationFrames = _frames;
        _prepared[page] = _host.ActualTheme;
        Reveal(page);
    }

    private void Reveal(Page page)
    {
        if (Current is { } previous && !ReferenceEquals(previous, page)) previous.Visibility = Visibility.Collapsed;
        Current = page;
        page.Visibility = Visibility.Visible;
        page.Opacity = 1;
        page.IsHitTestVisible = true;
    }

    private void CancelPending()
    {
        CompositionTarget.Rendering -= OnRendering;
        if (Pending is { } page)
        {
            page.Visibility = Visibility.Collapsed;
            page.Opacity = 1;
            Pending = null;
        }
    }

    public void Dispose()
    {
        CancelPending();
        _prepared.Clear();
        Current = null;
    }
}
