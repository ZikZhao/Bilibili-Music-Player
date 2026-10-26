using bilibili_music_player_windows.Services;

internal static class ResidentTabsTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("tabs retain actual pages and their resources across repeated switches", () =>
        {
            var creations = 0;
            using var tabs = new ResidentTabs<FakePage>(
                _ => { creations++; return new FakePage(); }, (page, active) => page.Activate(active));
            var search = tabs.Select("search");
            var player = search.Player;
            for (var i = 0; i < 5; i++)
            {
                search.IsPlaying = true;
                var favorites = tabs.Select("favorites");
                Check(!search.IsActive && !search.IsPlaying && favorites.IsActive, "The hidden tab did not pause.");
                Check(search.DisposeCount == 0, "Switching tabs disposed the hidden page.");
                var returned = tabs.Select("search");
                Check(ReferenceEquals(returned, search) && ReferenceEquals(returned.Player, player),
                    "Returning reconstructed the page or its resident resources.");
                Check(returned.IsActive && !returned.IsPlaying, "Returning unexpectedly resumed playback.");
            }
            Check(creations == 2 && tabs.Count == 2, "The factory ran again for an existing tab.");
            return Task.CompletedTask;
        }),
        ("reselecting the current tab does not interrupt its playback", () =>
        {
            var activations = 0;
            using var tabs = new ResidentTabs<FakePage>(_ => new FakePage(), (page, active) =>
            {
                activations++;
                page.Activate(active);
            });
            var search = tabs.Select("search");
            search.IsPlaying = true;
            tabs.Select("search");
            Check(activations == 1 && search.IsPlaying, "Reselecting caused an unnecessary pause/reactivation.");
            return Task.CompletedTask;
        }),
        ("failure to create a new tab leaves the current page active", () =>
        {
            using var tabs = new ResidentTabs<FakePage>(key => key == "broken" ? throw new IOException("failed") : new FakePage(),
                (page, active) => page.Activate(active));
            var search = tabs.Select("search");
            try { tabs.Select("broken"); }
            catch (IOException) { }
            Check(ReferenceEquals(tabs.ActivePage, search) && search.IsActive && tabs.Count == 1,
                "A failed factory hid or replaced the current tab.");
            return Task.CompletedTask;
        }),
        ("window closure disposes every resident page exactly once", () =>
        {
            var tabs = new ResidentTabs<FakePage>(_ => new FakePage(), (page, active) => page.Activate(active));
            var search = tabs.Select("search");
            var favorites = tabs.Select("favorites");
            tabs.Dispose();
            tabs.Dispose();
            Check(search.DisposeCount == 1 && favorites.DisposeCount == 1 && !favorites.IsActive && tabs.Count == 0,
                "Closing leaked pages or disposed them more than once.");
            var rejected = false;
            try { tabs.Select("search"); }
            catch (ObjectDisposedException) { rejected = true; }
            Check(rejected, "A closed host reconstructed a page.");
            return Task.CompletedTask;
        }),
        ("one failing cleanup cannot leak the other resident pages", () =>
        {
            var tabs = new ResidentTabs<FakePage>(key => new FakePage { FailDispose = key == "search" },
                (page, active) => page.Activate(active));
            var search = tabs.Select("search");
            var favorites = tabs.Select("favorites");
            var reported = false;
            try { tabs.Dispose(); }
            catch (AggregateException) { reported = true; }
            Check(reported && search.DisposeCount == 1 && favorites.DisposeCount == 1 && tabs.Count == 0,
                "A cleanup failure prevented the remaining pages from being disposed.");
            return Task.CompletedTask;
        }),
    ];

    private sealed class FakePage : IDisposable
    {
        public object Player { get; } = new();
        public bool IsActive { get; private set; }
        public bool IsPlaying { get; set; }
        public int DisposeCount { get; private set; }
        public bool FailDispose { get; init; }
        public void Activate(bool active)
        {
            IsActive = active;
            if (!active) IsPlaying = false;
        }
        public void Dispose()
        {
            DisposeCount++;
            if (FailDispose) throw new IOException("cleanup failed");
        }
    }
}
