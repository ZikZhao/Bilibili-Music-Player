using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Services;

public interface ILibraryCloud
{
    Task<LibrarySyncReply> PullAsync(Guid owner, Guid writer, long revision, string hash, bool snapshot, CancellationToken ct);
    Task<LibrarySyncReply> SubmitAsync(Guid owner, Guid writer, long revision, string hash, IReadOnlyList<LibraryCommand> commands, CancellationToken ct);
}
// Also reads the previous JSON format for non-destructive local migration.
public sealed record LocalLibrary(Guid? UserId, long CloudRevision, bool Pending, LibraryDocument Document, bool LegacyImported = false);

public sealed class LibraryRepository : IDisposable
{
    private readonly string _root;
    private readonly ILibraryCloud _cloud;
    private readonly LibrarySqliteStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _syncLock = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource _profile = new();
    private CancellationTokenSource? _debounce;
    private StoredLibrary _stored;
    private Task _syncTask = Task.CompletedTask;
    private bool _syncRequested, _disposed, _synchronize = true;
    private long _generation, _pendingSince;
    private DateTimeOffset _lastSuccess;
    private int _failures;
    public LocalLibrary State { get; private set; } = new(null, 0, false, new());
    public bool IsReady { get; private set; }
    public string Status { get; private set; } = "正在读取收藏…";
    public event EventHandler? Changed;

    public LibraryRepository(string root, ILibraryCloud cloud)
    {
        _root = root; _cloud = cloud; _store = new(root);
        var empty = new LibraryDocument(); _stored = new(null, 0, LibraryProtocol.Hash(empty), empty, empty, Guid.NewGuid(), 1, new(), false);
        _ = PollAsync();
    }
    private void Publish()
    {
        State = new(_stored.Owner, _stored.Revision, _stored.Pending.Count > 0, _stored.Document, _stored.LegacyImported);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task SwitchAsync(Guid? user, bool synchronize = true)
    {
        await _gate.WaitAsync();
        try
        {
            _profile.Cancel(); _profile.Dispose(); _profile = new(); _debounce?.Cancel(); _generation++;
            IsReady = false; _synchronize = synchronize; _pendingSince = 0;
            try { _stored = await Task.Run(() => _store.Read(user)) ?? await MigrateAsync(user, synchronize); }
            catch (Exception ex) when (user.HasValue && synchronize && ex is InvalidDataException or InvalidOperationException or ArgumentException)
            { _stored = await Task.Run(() => _store.Recover(user.Value)); }
            IsReady = true; Status = user is null || !synchronize ? "仅保存在本机" : _stored.Pending.Count > 0 ? "本机修改等待同步" : "等待同步";
            Publish();
        }
        finally { _gate.Release(); }
        if (user.HasValue && synchronize) _ = SyncAsync();
    }
    private async Task<StoredLibrary> MigrateAsync(Guid? user, bool synchronize)
    {
        var empty = new LibraryDocument();
        var state = new StoredLibrary(user, 0, LibraryProtocol.Hash(empty), empty, empty, Guid.NewGuid(), 1, new(), false);
        var path = Path.Combine(_root, user?.ToString("N") ?? "local", "library.json");
        if (File.Exists(path))
        {
            LocalLibrary old;
            try { old = JsonSerializer.Deserialize<LocalLibrary>(await File.ReadAllTextAsync(path), LibraryJson.Options)!; }
            catch (JsonException) { old = JsonSerializer.Deserialize<LocalLibrary>(await File.ReadAllTextAsync(path + ".bak"), LibraryJson.Options)!; }
            if (old.UserId != user) throw new InvalidDataException("收藏库账号不匹配。");
            var migrated = LibraryProtocol.Upgrade(old.Document);
            state = state with { LegacyImported = old.LegacyImported };
            if (user is null || !synchronize || !old.Pending)
                state = state with { Baseline = migrated, Document = migrated, Hash = LibraryProtocol.Hash(migrated), Revision = user.HasValue ? old.CloudRevision : 0 };
            else
            {
                // The old format has no clean base for reconstructing deletions.
                // Preserve its full content through an additive recovery import.
                var graph = new LibraryGraph(empty); Import(graph, migrated, "迁移前未同步收藏");
                var operations = graph.Commands.Select((c, i) => c with { Sequence = i + 1 }).ToList();
                state = state with { Document = graph.Document, Pending = operations, NextSequence = operations.Count + 1 };
            }
        }
        await Task.Run(() => _store.Save(state));
        // Keep legacy JSON and its .bak as migration recovery sources.
        return state;
    }
    public async Task EditAsync(Action<LibraryGraph> edit, bool markLegacyImported = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsReady) throw new InvalidOperationException("收藏库尚未就绪。");
            var before = _stored;
            var graph = await Task.Run(() => { var g = new LibraryGraph(before.Document); edit(g); return g; });
            var nextSequence = before.NextSequence;
            var additions = graph.Commands.Select(c => c with { Sequence = nextSequence++ }).ToList();
            var pending = before.Pending.Concat(additions).ToList();
            var next = before with { Document = graph.Document, Pending = pending, NextSequence = nextSequence, LegacyImported = before.LegacyImported || markLegacyImported };
            if (before.Owner is null || !_synchronize) next = next with { Baseline = graph.Document, Hash = LibraryProtocol.Hash(graph.Document), Pending = new() };
            await Task.Run(() => _store.Save(next)); _stored = next;
            if (_pendingSince == 0 && pending.Count > 0) _pendingSince = Stopwatch.GetTimestamp();
            Status = before.Owner is null || !_synchronize ? "已保存在本机" : "本机修改已保存，等待同步";
            Publish();
        }
        finally { _gate.Release(); }
        ScheduleSync();
    }
    private static void Import(LibraryGraph graph, LibraryDocument source, string name)
    {
        var container = graph.Create("playlist", name); var map = new Dictionary<Guid, Guid>(); var media = new Dictionary<Guid, Guid>();
        foreach (var item in source.Media) media[item.Id] = graph.Remember(item with { Id = Guid.NewGuid() });
        void Copy(Guid? parent, Guid into)
        {
            foreach (var item in LibraryGraph.Items(source, parent))
                if (item.NodeId is { } n) { var id = graph.Create("playlist", source.Nodes.Single(x => x.Id == n).Name, into); map[n] = id; Copy(n, id); }
                else if (item.MediaId is { } m) graph.AddMedia(into, new[] { media[m] });
        }
        Copy(null, container);
        // Preserve otherwise unfiled media in the import container as well.
        graph.AddMedia(container, source.Media.Where(m => !source.Entries.Any(e => e.MediaId == m.Id)).Select(m => media[m.Id]));
        foreach (var like in source.Likes) graph.SetLikeAt(media[like.MediaId], like.AddedAt);
    }
    public async Task ImportLocalAsync()
    {
        Guid? owner; StoredLibrary? guest;
        await _gate.WaitAsync();
        try
        {
            owner = _stored.Owner; if (owner is null) return;
            guest = await Task.Run(() => _store.Read(null));
        }
        finally { _gate.Release(); }
        if (guest is not null) await EditAsync(g =>
        {
            if (_stored.Owner != owner) throw new OperationCanceledException("账号已切换，已取消导入。");
            Import(g, guest.Document, "本机导入 " + DateTime.Now.ToString("yyyy-MM-dd"));
        });
    }
    private void ScheduleSync()
    {
        if (_disposed || !_synchronize || _stored.Owner is null || _stored.Pending.Count == 0) return;
        _debounce?.Cancel(); var cts = _debounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var elapsed = _pendingSince == 0 ? 0 : Stopwatch.GetElapsedTime(_pendingSince).TotalMilliseconds;
        var delay = Math.Max(0, Math.Min(400, 2000 - elapsed));
        _ = DelaySyncAsync(TimeSpan.FromMilliseconds(delay), cts.Token);
    }
    private async Task DelaySyncAsync(TimeSpan delay, CancellationToken ct)
    { try { await Task.Delay(delay, ct); await SyncAsync(); } catch (OperationCanceledException) { } }
    private async Task PollAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30)); while (await timer.WaitForNextTickAsync(_lifetime.Token))
                if (IsReady && _stored.Owner.HasValue && _synchronize) await SyncAsync();
        }
        catch (OperationCanceledException) { }
    }
    public Task RefreshAsync() => DateTimeOffset.UtcNow - _lastSuccess > TimeSpan.FromSeconds(15) ? SyncAsync() : Task.CompletedTask;
    public Task SyncAsync()
    {
        if (_disposed || !_synchronize || !IsReady || _stored.Owner is null) return Task.CompletedTask;
        TaskCompletionSource? completion = null;
        lock (_syncLock)
        {
            _syncRequested = true;
            if (!_syncTask.IsCompleted) return _syncTask;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _syncTask = completion.Task;
        }
        _ = RunSyncAsync(completion); return completion.Task;
    }
    private async Task RunSyncAsync(TaskCompletionSource completion)
    {
        var generation = _generation; var ct = _profile.Token;
        try
        {
            for (var turn = 0; turn < 12 && !_disposed && generation == _generation; turn++)
            {
                lock (_syncLock) _syncRequested = false;
                StoredLibrary sent;
                await _gate.WaitAsync(ct);
                try { sent = _stored; Status = "正在同步…"; Publish(); } finally { _gate.Release(); }
                var pull = await _cloud.PullAsync(sent.Owner!.Value, sent.Writer, sent.Revision, sent.Hash, false, ct);
                try { await AcceptAsync(pull, generation, ct); }
                catch (InvalidDataException)
                {
                    var fresh = await _cloud.PullAsync(sent.Owner.Value, sent.Writer, 0, "", true, ct);
                    await AcceptAsync(fresh, generation, ct);
                }
                await _gate.WaitAsync(ct);
                try { sent = _stored; } finally { _gate.Release(); }
                if (sent.Pending.Count > 0)
                {
                    var batch = new List<LibraryCommand>(); var size = 0;
                    foreach (var operation in sent.Pending.Take(128))
                    {
                        var bytes = JsonSerializer.SerializeToUtf8Bytes(operation, LibraryJson.Options).Length;
                        if (batch.Count > 0 && size + bytes > 900000) break;
                        batch.Add(operation); size += bytes;
                    }
                    var reply = await _cloud.SubmitAsync(sent.Owner!.Value, sent.Writer, sent.Revision, sent.Hash, batch, ct);
                    await AcceptAsync(reply, generation, ct);
                }
                _lastSuccess = DateTimeOffset.UtcNow; _failures = 0;
                lock (_syncLock) if (!_syncRequested && _stored.Pending.Count == 0) break;
            }
            if (generation == _generation) { Status = _stored.Pending.Count > 0 ? "本机修改等待下一批同步" : "已与云端同步"; Publish(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                Status = ex is InvalidDataException ? "基线校验失败，正在重新获取云端状态。本机操作已保留。" : "暂时无法同步，本机修改已保留，将自动重试。";
                Publish(); _failures++;
                _ = DelaySyncAsync(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(_failures, 5)))), _lifetime.Token);
            }
        }
        finally
        {
            completion.TrySetResult();
            if (!_disposed && generation != _generation && _stored.Owner.HasValue && _synchronize) _ = SyncAsync();
            else if (!_disposed && generation == _generation && _stored.Pending.Count > 0 && _failures == 0) ScheduleSync();
        }
    }
    private async Task AcceptAsync(LibrarySyncReply reply, long generation, CancellationToken ct)
    {
        StoredLibrary basis;
        await _gate.WaitAsync(ct);
        try
        {
            if (generation != _generation) return;
            basis = _stored;
        }
        finally { _gate.Release(); }
        var accepted = await Task.Run(() =>
        {
            var baseline = basis.Baseline; var revision = basis.Revision; var hash = basis.Hash;
            if (reply.Mode == "snapshot")
            {
                baseline = reply.Document ?? throw new InvalidDataException("Missing snapshot.");
                LibraryGraph.Validate(baseline);
                revision = reply.Revision; hash = LibraryProtocol.Hash(baseline);
            }
            else foreach (var change in reply.Changes)
            {
                if (change.Revision != revision + 1 || change.PreviousHash != hash) throw new InvalidDataException("Broken delta chain.");
                baseline = LibraryProtocol.Apply(baseline, change.Patch); revision = change.Revision; hash = LibraryProtocol.Hash(baseline);
                if (hash != change.Hash) throw new InvalidDataException("Delta content mismatch.");
            }
            if (revision != reply.Revision || hash != reply.Hash) throw new InvalidDataException("Snapshot version or digest mismatch.");
            if (reply.Acknowledged < 0) throw new InvalidDataException("Invalid operation acknowledgement.");
            return (baseline, revision, hash);
        }, ct);
        while (true)
        {
            StoredLibrary old;
            await _gate.WaitAsync(ct);
            try { if (generation != _generation) return; old = _stored; } finally { _gate.Release(); }
            var pending = old.Pending.Where(c => c.Sequence > reply.Acknowledged).ToList();
            var working = await Task.Run(() => { var d = accepted.baseline; foreach (var operation in pending) d = LibraryGraph.Replay(d, operation); return d; }, ct);
            var next = old with { Baseline = accepted.baseline, Document = working, Revision = accepted.revision, Hash = accepted.hash, Pending = pending, NextSequence = Math.Max(old.NextSequence, reply.Acknowledged + 1) };
            await _gate.WaitAsync(ct);
            try
            {
                if (generation != _generation) return;
                if (!ReferenceEquals(old, _stored)) continue; // Replay a newly appended tail before publishing.
                await Task.Run(() => _store.Save(next), ct); _stored = next;
                if (pending.Count == 0) _pendingSince = 0;
                Publish(); return;
            }
            finally { _gate.Release(); }
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); _profile.Cancel(); _debounce?.Cancel();
        // Defer closing the connection until any local transaction releases it.
        _ = CloseStoreAsync();
    }
    private async Task CloseStoreAsync() { await _gate.WaitAsync(); try { _store.Dispose(); } finally { _gate.Release(); } }
}
