using System;
using System.Collections.Generic;

namespace bilibili_music_player_windows.Services;

/// <summary>
/// Owns the actual tab instances for the lifetime of the window. Selecting a tab
/// changes its visibility/activation; it never removes or reconstructs another tab.
/// </summary>
public sealed class ResidentTabs<T> : IDisposable where T : class
{
    private readonly Dictionary<string, T> _tabs = new(StringComparer.Ordinal);
    private readonly Func<string, T> _create;
    private readonly Action<T, bool> _activate;
    private bool _disposed;

    public ResidentTabs(Func<string, T> create, Action<T, bool> activate)
    {
        _create = create;
        _activate = activate;
    }

    public string? ActiveKey { get; private set; }
    public T? ActivePage => ActiveKey is { } key ? _tabs[key] : null;
    public int Count => _tabs.Count;

    public T Select(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ActiveKey == key) return _tabs[key];

        // Construct before hiding the current tab, so a failed creation leaves it usable.
        if (!_tabs.TryGetValue(key, out var next))
        {
            next = _create(key);
            _tabs.Add(key, next);
        }
        if (ActivePage is { } current) _activate(current, false);
        ActiveKey = key;
        _activate(next, true);
        return next;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? errors = null;
        if (ActivePage is { } active)
        {
            try { _activate(active, false); }
            catch (Exception ex) { (errors ??= new()).Add(ex); }
        }
        foreach (var page in _tabs.Values)
        {
            try { (page as IDisposable)?.Dispose(); }
            catch (Exception ex) { (errors ??= new()).Add(ex); }
        }
        _tabs.Clear();
        ActiveKey = null;
        if (errors is not null) throw new AggregateException(errors);
    }
}

/// <summary>A resident page can pause its activity without unloading its controls.</summary>
public interface IResidentTab
{
    void SetTabActive(bool active);
}
