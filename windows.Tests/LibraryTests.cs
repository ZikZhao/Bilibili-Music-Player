using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Services;

internal static class LibraryTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All => new (string, Func<Task>)[]
    {
        ("pinned video parts keep their CID and cannot share a different part's cache", () =>
        {
            Check(AudioSourceIdentity.ResolveCid(123, 456) == 123);
            Check(AudioSourceIdentity.ResolveCid(0, 456) == 456);
            Check(AudioSourceIdentity.CacheKey("BV123", 123) != AudioSourceIdentity.CacheKey("BV123", 456));
            Check(AudioSourceIdentity.CacheKey("BV123", 123) != AudioSourceIdentity.CacheKey("BV123", 0));
            return Task.CompletedTask;
        }),
        ("default collection preserves original timestamps on repeated collection", () =>
        {
            var g = Graph(); var a = Media(g, "a");
            var original = new LibraryLike(a, new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));
            g.Document.Likes.Add(original);
            g.Like(a, true); g.Like(a, true);
            Check(g.Document.Likes.Count == 1 && g.Document.Likes[0] == original);
            return Task.CompletedTask;
        }),
        ("default timeline is newest first with stable reverse insertion order for equal timestamps", () =>
        {
            var g = Graph(); var a = Media(g, "a"); var b = Media(g, "b"); var c = Media(g, "c");
            var old = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            g.Document.Likes.Add(new(a, old)); g.Document.Likes.Add(new(b, old.AddDays(1))); g.Document.Likes.Add(new(c, old.AddDays(1)));
            Eq(new[] { c, b, a }, g.Expand(null, likes: true).Select(m => m.Id));
            return Task.CompletedTask;
        }),
        ("collecting again after removal creates a new timeline date without changing other playlists", () =>
        {
            var g = Graph(); var a = Media(g, "a"); var p = g.Create("playlist", "regular"); g.AddMedia(p, new[] { a });
            var old = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            g.Document.Likes.Add(new(a, old));
            g.Like(a, false); Check(g.Document.Likes.Count == 0 && g.Expand(p).Single().Id == a);
            g.Like(a, true); Check(g.Document.Likes.Single().AddedAt > old && g.Expand(p).Single().Id == a);
            return Task.CompletedTask;
        }),
        ("dynamic playlist links follow edits and preserve expansion order", () =>
        {
            var g = Graph(); var a = Media(g, "a"); var b = Media(g, "b"); var c = Media(g, "c");
            var source = g.Create("playlist", "source"); var target = g.Create("playlist", "target");
            g.AddMedia(source, new[] { b }); g.AddMedia(target, new[] { a }); g.Link(target, source); g.AddMedia(target, new[] { c });
            Eq(new[] { a, b, c }, g.Expand(target).Select(m => m.Id));
            var d = Media(g, "d"); g.AddMedia(source, new[] { d });
            Eq(new[] { a, b, d, c }, g.Expand(target).Select(m => m.Id)); return Task.CompletedTask;
        }),
        ("queue snapshots are unaffected by later linked playlist edits", () =>
        {
            var g = Graph(); var p = g.Create("playlist", "p"); var a = Media(g, "a"); g.AddMedia(p, new[] { a });
            var queue = g.Expand(p).ToArray(); g.AddMedia(p, new[] { Media(g, "b") });
            Check(queue.Length == 1 && g.Expand(p).Count == 2); return Task.CompletedTask;
        }),
        ("nested folders aggregate playlists and deduplicate exact audio versions", () =>
        {
            var g = Graph(); var f = g.Create("folder", "f"); var sub = g.Create("folder", "sub", f);
            var p = g.Create("playlist", "p", sub); var q = g.Create("playlist", "q", f); var a = Media(g, "a");
            g.AddMedia(p, new[] { a }); g.AddMedia(q, new[] { a, Media(g, "b") }); Check(g.Expand(f).Count == 2); return Task.CompletedTask;
        }),
        ("same title with different videos or parts remains distinct", () =>
        {
            var g = Graph(); var a = new LibraryMedia(Guid.NewGuid(), "BV1", 1, "same", "", "", 1, "0:01");
            var id = g.Remember(a); Check(g.Remember(a with { Id = Guid.NewGuid() }) == id);
            g.Remember(a with { Id = Guid.NewGuid(), Cid = 2 }); g.Remember(a with { Id = Guid.NewGuid(), Bvid = "BV2" });
            Check(g.Document.Media.Count == 3); return Task.CompletedTask;
        }),
        ("direct and indirect playlist cycles are rejected atomically", () =>
        {
            var g = Graph(); var a = g.Create("playlist", "a"); var b = g.Create("playlist", "b"); var c = g.Create("playlist", "c");
            Reject(() => g.Link(a, a)); g.Link(a, b); g.Link(b, c); Reject(() => g.Link(c, a)); Check(g.Document.Entries.Count == 2); return Task.CompletedTask;
        }),
        ("moving a folder under its own descendant leaves the tree intact", () =>
        {
            var g = Graph(); var a = g.Create("folder", "a"); var b = g.Create("folder", "b", a);
            Reject(() => g.Move(a, b)); Check(g.Document.Nodes.Single(n => n.Id == a).ParentId is null); return Task.CompletedTask;
        }),
        ("dissolving folders and deleting linked playlists preserve media and likes", () =>
        {
            var g = Graph(); var f = g.Create("folder", "f"); var p = g.Create("playlist", "p", f); var q = g.Create("playlist", "q");
            var a = Media(g, "a"); g.Like(a, true); g.AddMedia(p, new[] { a }); g.Link(q, p);
            g.Delete(f); Check(g.Document.Nodes.Single(n => n.Id == p).ParentId is null); g.Delete(p);
            Check(g.Document.Entries.Count == 0 && g.Document.Media.Count == 1 && g.Document.Likes.Count == 1); return Task.CompletedTask;
        }),
        ("unliking never removes playlist membership and copies do not follow source edits", () =>
        {
            var g = Graph(); var a = Media(g, "a"); var p = g.Create("playlist", "p"); var q = g.Create("playlist", "q");
            g.Like(a, true); g.AddMedia(p, new[] { a }); g.AddMedia(q, g.Expand(p).Select(m => m.Id)); g.Like(a, false);
            g.AddMedia(p, new[] { Media(g, "b") }); Check(g.Expand(q).Count == 1 && g.Expand(p).Count == 2); return Task.CompletedTask;
        }),
        ("explicit library removal unlinks only that audio version everywhere", () =>
        {
            var g = Graph(); var a = Media(g, "a"); var b = Media(g, "b"); var p = g.Create("playlist", "p"); var q = g.Create("playlist", "q");
            g.Like(a, true); g.AddMedia(p, new[] { a, b }); g.Link(q, p); g.RemoveMedia(a);
            Check(g.Document.Likes.Count == 0); Eq(new[] { b }, g.Expand(q).Select(m => m.Id)); return Task.CompletedTask;
        }),
        ("reordering a link moves its entire expansion without changing the source", () =>
        {
            var g = Graph(); var a = Media(g, "a"); var b = Media(g, "b"); var p = g.Create("playlist", "p"); var q = g.Create("playlist", "q");
            g.AddMedia(p, new[] { b }); g.AddMedia(q, new[] { a }); g.Link(q, p);
            g.ReorderEntry(g.Document.Entries.Single(e => e.PlaylistId == q && e.LinkedPlaylistId.HasValue).Id, -1);
            Eq(new[] { b, a }, g.Expand(q).Select(m => m.Id)); Eq(new[] { b }, g.Expand(p).Select(m => m.Id)); return Task.CompletedTask;
        }),
        ("deep nesting is bounded even when ancestors were already visited", () =>
        {
            var g = Graph(); Guid? parent = null;
            for (var i = 0; i < 65; i++) parent = g.Create("folder", i.ToString(), parent);
            Reject(() => g.Create("folder", "too deep", parent)); return Task.CompletedTask;
        }),
        ("account and guest libraries persist independently across restarts", async () =>
        {
            using var folder = new TestFolder(); var cloud = new FakeCloud { Offline = true }; using var repo = new LibraryRepository(folder.Path, cloud);
            await repo.SwitchAsync(null); await repo.EditAsync(g => g.Create("playlist", "guest"));
            var user = Guid.NewGuid(); await repo.SwitchAsync(user); Check(repo.State.Document.Nodes.Count == 0);
            await repo.EditAsync(g => g.Create("folder", "account"));
            using var restarted = new LibraryRepository(folder.Path, cloud); await restarted.SwitchAsync(null);
            Check(restarted.State.Document.Nodes.Single().Name == "guest"); await restarted.SwitchAsync(user);
            Check(restarted.State.Document.Nodes.Single().Name == "account");
        }),
        ("optimistic cloud conflicts preserve local edits until explicit resolution", async () =>
        {
            using var folder = new TestFolder(); var cloud = new FakeCloud(); using var repo = new LibraryRepository(folder.Path, cloud);
            await repo.SwitchAsync(Guid.NewGuid()); await repo.SyncAsync();
            await repo.EditAsync(g => g.Create("playlist", "local"));
            var remote = Graph(); remote.Create("folder", "remote"); cloud.Current = new(2, remote.Document);
            await repo.SyncAsync(); Check(repo.HasConflict && repo.State.Document.Nodes.Single().Name == "local");
            await repo.SyncAsync(false); Check(!repo.HasConflict && repo.State.Document.Nodes.Single().Name == "remote");
            Check(Directory.EnumerateFiles(folder.Path, "*.conflict-local", SearchOption.AllDirectories).Any());
        }),
        ("lost cloud write acknowledgements do not create false conflicts", async () =>
        {
            using var folder = new TestFolder(); var cloud = new FakeCloud(); using var repo = new LibraryRepository(folder.Path, cloud);
            await repo.SwitchAsync(Guid.NewGuid()); await repo.SyncAsync(); await repo.EditAsync(g => g.Create("playlist", "local"));
            cloud.LoseAck = true; await repo.SyncAsync(); Check(repo.State.Pending && cloud.Current.Revision == 1);
            cloud.LoseAck = false; await repo.SyncAsync(); Check(!repo.HasConflict && !repo.State.Pending && repo.State.CloudRevision == 1);
        }),
        ("late responses after switching accounts cannot overwrite the next account", async () =>
        {
            using var folder = new TestFolder(); var cloud = new FakeCloud(); using var repo = new LibraryRepository(folder.Path, cloud);
            var a = Guid.NewGuid(); var b = Guid.NewGuid(); await repo.SwitchAsync(a); await repo.SyncAsync();
            cloud.BlockRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = repo.SyncAsync(); await cloud.ReadStarted.Task;
            await repo.SwitchAsync(b); cloud.BlockRead.SetResult(new(5, new LibraryDocument { Nodes = new() { new(Guid.NewGuid(), "playlist", "old account", null, 0) } }));
            await pending; cloud.BlockRead = null; await repo.SyncAsync(); Check(repo.State.UserId == b && repo.State.Document.Nodes.Count == 0);
        }),
        ("invalid or corrupt local documents never replace a valid backup", async () =>
        {
            using var folder = new TestFolder(); using var repo = new LibraryRepository(folder.Path, new FakeCloud()); await repo.SwitchAsync(null);
            await repo.EditAsync(g => g.Create("playlist", "safe")); await repo.EditAsync(g => g.Create("playlist", "latest"));
            await File.WriteAllTextAsync(System.IO.Path.Combine(folder.Path, "local", "library.json"), "broken json");
            using var restarted = new LibraryRepository(folder.Path, new FakeCloud()); await restarted.SwitchAsync(null);
            Check(restarted.State.Document.Nodes.Single().Name == "safe");
            await restarted.EditAsync(g => g.Create("playlist", "after recovery"));
            var backup = System.Text.Json.JsonSerializer.Deserialize<LocalLibrary>(
                await File.ReadAllTextAsync(System.IO.Path.Combine(folder.Path, "local", "library.json.bak")), LibraryJson.Options);
            Check(backup!.Document.Nodes.Single().Name == "safe");
            Check(Directory.EnumerateFiles(folder.Path, "*.unreadable-*", SearchOption.AllDirectories).Any());
        }),
    };
    private static LibraryGraph Graph() => new(new());
    private static Guid Media(LibraryGraph g, string key) => g.Remember(new(Guid.NewGuid(), "BV" + key, 1, key, "", "", 1, "0:01"));
    private static void Check(bool ok) { if (!ok) throw new Exception("Library regression failed"); }
    private static void Eq(IEnumerable<Guid> a, IEnumerable<Guid> b) => Check(a.SequenceEqual(b));
    private static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejected graph edit"); }

    private sealed class FakeCloud : ILibraryCloud
    {
        public bool Offline, LoseAck;
        public CloudLibrary Current = new(0, new());
        public TaskCompletionSource<CloudLibrary>? BlockRead;
        public TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CloudLibrary> ReadAsync(Guid owner, CancellationToken ct)
        { if (Offline) throw new IOException(); if (BlockRead is { } block) { ReadStarted.TrySetResult(); return block.Task; } return Task.FromResult(Current); }
        public Task<CloudLibrary> WriteAsync(Guid owner, long revision, LibraryDocument doc, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (Offline) throw new IOException(); if (revision != Current.Revision) throw new LibraryConflictException();
            Current = new(revision + 1, doc.Copy()); if (LoseAck) throw new IOException(); return Task.FromResult(Current);
        }
    }
    private sealed class TestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BiliMusicTests", Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            var boundary = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BiliMusicTests")) + System.IO.Path.DirectorySeparatorChar;
            var target = System.IO.Path.GetFullPath(Path);
            if (!target.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture cleanup path.");
            // Debounced fixture operations may still be unwinding; no user data
            // is stored here and all paths are generated below this test root.
            try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch (IOException) { }
        }
    }
}
