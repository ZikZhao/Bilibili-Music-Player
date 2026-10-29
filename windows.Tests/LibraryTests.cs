using System.Text.Json;
using Microsoft.Data.Sqlite;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Services;

internal static class LibraryTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All => new (string, Func<Task>)[]
    {
        ("fractional ranks prepend one million times without renumbering",()=>
        {
            string? rank=null;
            for(var i=0;i<1_000_000;i++) {var next=FractionalRank.Between(null,rank);Check(rank is null||string.CompareOrdinal(next,rank)<0);rank=next;}
            Check(rank!.Length<10);FractionalRank.Validate(rank);
            var left="a0";var right="a1";
            for(var i=0;i<100;i++){var mid=FractionalRank.Between(left,right);Check(string.CompareOrdinal(left,mid)<0&&string.CompareOrdinal(mid,right)<0);right=mid;}
            return Task.CompletedTask;
        }),
        ("mixed nested playlists expand in rank order and keep queue snapshots independent",()=>
        {
            var g=Graph();var p=g.Create("playlist","parent");var a=Media(g,"a");g.AddMedia(p,new[]{a});
            var child=g.Create("playlist","child",p);var b=Media(g,"b");g.AddMedia(child,new[]{b,a});var c=Media(g,"c");g.AddMedia(p,new[]{c});
            Eq(new[]{a,b,c},g.Expand(p).Select(m=>m.Id));var snapshot=g.Expand(p).ToArray();
            g.ReorderNode(child,-1);Eq(new[]{b,a,c},g.Expand(p).Select(m=>m.Id));Eq(new[]{a,b,c},snapshot.Select(m=>m.Id));
            Check(g.Document.Nodes.All(n=>n.Kind=="playlist"));return Task.CompletedTask;
        }),
        ("timeline retains dates on duplicate likes and creates a new point after removal",()=>
        {
            var g=Graph();var a=Media(g,"a");var b=Media(g,"b");var c=Media(g,"c");var time=DateTimeOffset.FromUnixTimeMilliseconds(1000);
            g.SetLikeAt(a,time);g.SetLikeAt(b,time.AddDays(1));g.SetLikeAt(c,time.AddDays(1));var original=g.Document.Likes.Single(l=>l.MediaId==a);
            g.Like(a,true);Check(g.Document.Likes.Single(l=>l.MediaId==a)==original);Eq(new[]{c,b,a},g.Expand(null,true).Select(m=>m.Id));
            var p=g.Create("playlist","p");g.AddMedia(p,new[]{a});g.Like(a,false);g.Like(a,true);
            Check(g.Document.Likes.Single(l=>l.MediaId==a).AddedAt>time&&g.Expand(p).Single().Id==a);return Task.CompletedTask;
        }),
        ("recursive deletion leaves other memberships and default favorites intact",()=>
        {
            var g=Graph();var p=g.Create("playlist","p");var sub=g.Create("playlist","sub",p);var q=g.Create("playlist","q");var a=Media(g,"a");
            g.AddMedia(sub,new[]{a});g.AddMedia(q,new[]{a});g.Like(a,true);g.Delete(p);
            Check(g.Document.Nodes.Single().Id==q&&g.Document.Entries.Count==1&&g.Document.Likes.Count==1&&g.Document.Media.Count==1);
            g.AddMedia(sub,new[]{a});Check(g.Document.Entries.Count==1);g.Move(q,q);Check(g.Document.Nodes.Single().ParentId is null);return Task.CompletedTask;
        }),
        ("audio identity is BVID and CID and cancellation survives duplicate entry IDs",()=>
        {
            var g=Graph();var a=Media(g,"a");Check(g.Remember(g.Document.Media.Single() with {Id=Guid.NewGuid()})==a);
            var b=g.Remember(g.Document.Media.Single() with {Id=Guid.NewGuid(),Cid=2});Check(a!=b);
            var p=g.Create("playlist","p");var baseline=g.Document.Copy();g.AddMedia(p,new[]{a});g.RemoveEntry(g.Document.Entries.Single().Id);
            var aOps=g.Commands.TakeLast(2).ToArray();var remote=new LibraryGraph(baseline);remote.AddMedia(p,new[]{a});var d=remote.Document;
            foreach(var op in aOps)d=LibraryGraph.Replay(d,op);Check(d.Entries.Count==0);
            Check(AudioSourceIdentity.CacheKey("BV1",1)!=AudioSourceIdentity.CacheKey("BV1",2));return Task.CompletedTask;
        }),
        ("changes patch the full graph atomically with a canonical digest",()=>
        {
            var g=Graph();var p=g.Create("playlist","p");var a=Media(g,"a");g.AddMedia(p,new[]{a});var before=g.Document.Copy();g.Delete(p);g.RemoveMedia(a);
            var after=LibraryProtocol.Apply(before,LibraryProtocol.Diff(before,g.Document));Check(LibraryProtocol.Hash(after)==LibraryProtocol.Hash(g.Document));
            var path=Path.Combine(AppContext.BaseDirectory,"library-parity-fixture.json");
            using var fixture=JsonDocument.Parse(File.ReadAllText(path));var d=fixture.RootElement.GetProperty("document").Deserialize<LibraryDocument>(LibraryJson.Options)!;LibraryGraph.Validate(d);Check(LibraryProtocol.Hash(d)==fixture.RootElement.GetProperty("hash").GetString());
            foreach(var vector in fixture.RootElement.GetProperty("ranks").EnumerateArray())
                Check(FractionalRank.Between(vector.GetProperty("a").GetString(),vector.GetProperty("b").GetString())==vector.GetProperty("result").GetString());
            return Task.CompletedTask;
        }),
        ("SQLite base work and outbox persist atomically and work row edits are ignored",async()=>
        {
            using var folder=new TestFolder();var owner=Guid.NewGuid();var cloud=new FakeCloud{Offline=true};
            using(var repo=new LibraryRepository(folder.Path,cloud)){await repo.SwitchAsync(owner);await repo.SyncAsync();await repo.EditAsync(g=>g.Create("playlist","pending"));}
            using(var db=Db(folder.Path)){using var cmd=db.CreateCommand();cmd.CommandText="UPDATE playlists SET name='tampered work' WHERE layer='work' AND kind='playlist'";cmd.ExecuteNonQuery();}
            using var restart=new LibraryRepository(folder.Path,cloud);await restart.SwitchAsync(owner);Check(restart.State.Document.Nodes.Single().Name=="pending"&&restart.State.Pending);
            cloud.Offline=false;await restart.SyncAsync();Check(!restart.State.Pending&&cloud.Doc(owner).Nodes.Single().Name=="pending");
            using var store=new LibrarySqliteStore(folder.Path);var saved=store.Read(owner)!;Check(saved.Pending.Count==0&&saved.Revision==1);
        }),
        ("editing while HTTP is blocked preserves the unsent tail and never waits for the network",async()=>
        {
            using var folder=new TestFolder();var cloud=new FakeCloud();var user=Guid.NewGuid();using var repo=new LibraryRepository(folder.Path,cloud);await repo.SwitchAsync(user);await repo.SyncAsync();
            await repo.EditAsync(g=>g.Create("playlist","first"));cloud.SubmitGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var sync=repo.SyncAsync();await cloud.SubmitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await repo.EditAsync(g=>g.Create("playlist","tail")).WaitAsync(TimeSpan.FromSeconds(2));Check(repo.State.Document.Nodes.Count==2);
            cloud.SubmitGate.SetResult();await sync.WaitAsync(TimeSpan.FromSeconds(5));Check(!repo.State.Pending&&cloud.Doc(user).Nodes.Count==2);
        }),
        ("two replicas rebase pending edits and lost acknowledgements are idempotent",async()=>
        {
            using var fa=new TestFolder();using var fb=new TestFolder();var cloud=new FakeCloud();var user=Guid.NewGuid();
            using var a=new LibraryRepository(fa.Path,cloud);using var b=new LibraryRepository(fb.Path,cloud);await a.SwitchAsync(user);await a.SyncAsync();await b.SwitchAsync(user);await b.SyncAsync();
            await a.EditAsync(g=>g.Create("playlist","A"));await b.EditAsync(g=>g.Create("playlist","B"));await b.SyncAsync();
            cloud.LoseAck=true;await a.SyncAsync();Check(a.State.Pending&&cloud.Doc(user).Nodes.Count==2);cloud.LoseAck=false;await a.SyncAsync();await b.SyncAsync();
            Check(!a.State.Pending&&!b.State.Pending&&a.State.CloudRevision==2&&b.State.CloudRevision==2);
            Check(LibraryProtocol.Hash(a.State.Document)==LibraryProtocol.Hash(b.State.Document));
        }),
        ("local baseline corruption fetches a fresh cloud snapshot and retains the outbox",async()=>
        {
            using var f=new TestFolder();var cloud=new FakeCloud();var user=Guid.NewGuid();
            using(var repo=new LibraryRepository(f.Path,cloud)){await repo.SwitchAsync(user);await repo.SyncAsync();await repo.EditAsync(g=>g.Create("playlist","remote"));await repo.SyncAsync();cloud.Offline=true;await repo.EditAsync(g=>g.Create("playlist","offline"));}
            using(var db=Db(f.Path)){using var c=db.CreateCommand();c.CommandText="UPDATE playlists SET name='bad base' WHERE layer='base' AND kind='playlist'";c.ExecuteNonQuery();}
            cloud.Offline=false;using var restart=new LibraryRepository(f.Path,cloud);await restart.SwitchAsync(user);await restart.SyncAsync();
            Check(restart.State.Document.Nodes.Any(n=>n.Name=="remote")&&restart.State.Document.Nodes.Any(n=>n.Name=="offline")&&!restart.State.Pending);
            Check(Directory.EnumerateFiles(f.Path,"*.unverified-*").Any());
        }),
        ("late replies cannot overwrite a newly selected account and guests stay local",async()=>
        {
            using var f=new TestFolder();var cloud=new FakeCloud();using var repo=new LibraryRepository(f.Path,cloud);var a=Guid.NewGuid();var b=Guid.NewGuid();
            await repo.SwitchAsync(null);await repo.EditAsync(g=>g.Create("playlist","guest"));Check(cloud.Calls==0);
            await repo.SwitchAsync(a);await repo.SyncAsync();cloud.PullGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var sync=repo.SyncAsync();await cloud.PullStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));await repo.SwitchAsync(b);cloud.PullGate.SetResult();await sync;cloud.PullGate=null;await repo.SyncAsync();
            Check(repo.State.UserId==b&&repo.State.Document.Nodes.Count==0);await repo.SwitchAsync(null);Check(repo.State.Document.Nodes.Single().Name=="guest");
        }),
        ("SQLite transaction failure rolls back rows and durable intents together",()=>
        {
            using var f=new TestFolder();using var store=new LibrarySqliteStore(f.Path);var empty=new LibraryDocument();var initial=new StoredLibrary(null,0,LibraryProtocol.Hash(empty),empty,empty,Guid.NewGuid(),1,new(),false);store.Save(initial);
            var g=Graph();g.Create("playlist","must roll back");var bad=new LibraryCommand{Id=Guid.NewGuid(),Sequence=1,Type="delete"};
            try{store.Save(initial with{Document=g.Document,Pending=new(){bad,bad with{Sequence=2}},NextSequence=3});throw new Exception("Expected unique constraint failure");}catch(SqliteException){}
            var read=store.Read(null)!;Check(read.Document.Nodes.Count==0&&read.Pending.Count==0&&read.NextSequence==1);return Task.CompletedTask;
        }),
        ("guest import preserves nested order and timeline while a cloud request is in flight",async()=>
        {
            using var folder=new TestFolder();var cloud=new FakeCloud();using var repo=new LibraryRepository(folder.Path,cloud);
            await repo.SwitchAsync(null);var time=DateTimeOffset.FromUnixTimeMilliseconds(1234567890123);
            await repo.EditAsync(g=>{var p=g.Create("playlist","guest parent");var sub=g.Create("playlist","guest child",p);var a=Media(g,"guest");g.AddMedia(sub,new[]{a});g.SetLikeAt(a,time);});
            var user=Guid.NewGuid();await repo.SwitchAsync(user);await repo.SyncAsync();cloud.PullGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var sync=repo.SyncAsync();await cloud.PullStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await repo.ImportLocalAsync().WaitAsync(TimeSpan.FromSeconds(2));Check(repo.State.Document.Nodes.Count==3&&repo.State.Document.Likes.Single().AddedAt==time);
            cloud.PullGate.SetResult();await sync;Check(!repo.State.Pending&&cloud.Doc(user).Entries.Count==1);
            await repo.SwitchAsync(null);Check(repo.State.Document.Nodes.Count==2&&repo.State.Document.Likes.Single().AddedAt==time);
        }),
    };
    private static LibraryGraph Graph() => new(new());
    private static Guid Media(LibraryGraph g, string key) => g.Remember(new(Guid.NewGuid(), "BV" + key, 1, key, "歌手", "", 120, "02:00"));
    private static void Check(bool value) { if (!value) throw new Exception("Library regression failed"); }
    private static void Eq(IEnumerable<Guid> a, IEnumerable<Guid> b) => Check(a.SequenceEqual(b));
    private static SqliteConnection Db(string root) { var db = new SqliteConnection("Data Source=" + Path.Combine(root, "library.db") + ";Pooling=False"); db.Open(); return db; }
    private sealed class FakeCloud : ILibraryCloud
    {
        private sealed class State { public LibraryDocument Document = new(); public long Revision; public Dictionary<Guid, long> Ack = new(); public List<LibraryChange> Changes = new(); }
        private readonly Dictionary<Guid, State> _states = new(); private readonly object _lock = new();
        public bool Offline, LoseAck; public int Calls;
        public TaskCompletionSource? SubmitGate, PullGate;
        public TaskCompletionSource SubmitStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), PullStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private State For(Guid user) { if (!_states.TryGetValue(user, out var s)) _states[user] = s = new(); return s; }
        public LibraryDocument Doc(Guid user) { lock (_lock) return For(user).Document.Copy(); }
        public async Task<LibrarySyncReply> PullAsync(Guid owner, Guid writer, long revision, string hash, bool snapshot, CancellationToken ct)
        {
            Calls++; if (Offline) throw new IOException("offline");
            if (PullGate is { } gate) { PullStarted.TrySetResult(); await gate.Task; }
            lock (_lock) return Reply(For(owner), writer, revision, hash, snapshot);
        }
        public async Task<LibrarySyncReply> SubmitAsync(Guid owner, Guid writer, long revision, string hash, IReadOnlyList<LibraryCommand> commands, CancellationToken ct)
        {
            Calls++; if (Offline) throw new IOException("offline");
            if (SubmitGate is { } gate) { SubmitStarted.TrySetResult(); await gate.Task; }
            ct.ThrowIfCancellationRequested();
            lock (_lock)
            {
                var s = For(owner); if (revision != s.Revision || hash != LibraryProtocol.Hash(s.Document)) return Reply(s, writer, revision, hash, false);
                foreach (var c in commands)
                {
                    var ack = s.Ack.GetValueOrDefault(writer); if (c.Sequence <= ack) continue; Check(c.Sequence == ack + 1);
                    var before = s.Document; var previous = LibraryProtocol.Hash(before); s.Document = LibraryGraph.Replay(before, c, ++s.Revision);
                    s.Changes.Add(new(s.Revision, previous, LibraryProtocol.Hash(s.Document), LibraryProtocol.Diff(before, s.Document))); s.Ack[writer] = c.Sequence;
                }
                if (LoseAck) throw new IOException("lost ack"); return Reply(s, writer, revision, hash, false);
            }
        }
        private static LibrarySyncReply Reply(State s, Guid writer, long revision, string hash, bool snapshot)
        {
            var now = LibraryProtocol.Hash(s.Document); var changes = s.Changes.Where(c => c.Revision > revision).ToList(); var ack = s.Ack.GetValueOrDefault(writer);
            if (!snapshot && revision == s.Revision && hash == now) return new("unchanged", s.Revision, now, null, new(), ack);
            if (!snapshot && changes.Count > 0 && changes[0].Revision == revision + 1 && changes[0].PreviousHash == hash) return new("delta", s.Revision, now, null, changes, ack);
            return new("snapshot", s.Revision, now, s.Document.Copy(), new(), ack);
        }
    }
    private sealed class TestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BiliMusicTests", Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            var boundary = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BiliMusicTests")) + System.IO.Path.DirectorySeparatorChar;
            var target = System.IO.Path.GetFullPath(Path); if (!target.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe fixture path");
            try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch (IOException) { }
        }
    }
}
