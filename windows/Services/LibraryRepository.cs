using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Services;

public interface ILibraryCloud
{
    Task<CloudLibrary> ReadAsync(Guid owner, CancellationToken ct);
    Task<CloudLibrary> WriteAsync(Guid owner, long expectedRevision, LibraryDocument document, CancellationToken ct);
}

public sealed class LibraryConflictException : Exception
{
    public LibraryConflictException() : base("云端收藏已在其他设备更新，本机修改已保留。请选择要保留的版本。") { }
}

public sealed record LocalLibrary(Guid? UserId, long CloudRevision, bool Pending, LibraryDocument Document,
    bool LegacyImported = false);

/// <summary>Account-scoped atomic local files, optimistic cloud commits and explicit conflict resolution.</summary>
public sealed class LibraryRepository : IDisposable
{
    private readonly string _root;
    private readonly ILibraryCloud _cloud;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private CancellationTokenSource _profileCts = new();
    private CancellationTokenSource? _debounce;
    private long _generation;
    private long _editVersion;
    private bool _synchronize = true;
    private bool _disposed;
    public LocalLibrary State { get; private set; } = new(null, 0, false, new());
    public bool IsReady { get; private set; }
    public bool HasConflict { get; private set; }
    public string Status { get; private set; } = "正在读取收藏…";
    public event EventHandler? Changed;

    public LibraryRepository(string root, ILibraryCloud cloud) { _root = root; _cloud = cloud; }
    private string FilePath(Guid? id) => Path.Combine(_root, id?.ToString("N") ?? "local", "library.json");

    public async Task SwitchAsync(Guid? user, bool synchronize = true)
    {
        await _gate.WaitAsync();
        try
        {
            _profileCts.Cancel(); _profileCts.Dispose(); _profileCts = new();
            _debounce?.Cancel();
            _generation++; _editVersion = 0; IsReady = false; HasConflict = false;
            _synchronize = synchronize;
            State = new(user, 0, false, new());
            var path = FilePath(user);
            if (File.Exists(path))
            {
                try { State = await ReadFileAsync(path, user); }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
                {
                    // Never replace an unreadable library with an empty document.
                    State = await ReadFileAsync(path + ".bak", user);
                    // Preserve the damaged bytes, then restore the primary too.
                    // Otherwise the next save would back up the damaged primary
                    // over the only valid backup before replacing it.
                    File.Copy(path, path + ".unreadable-" + Guid.NewGuid().ToString("N"));
                    File.Copy(path + ".bak", path, overwrite: true);
                    Status = "已从本机备份恢复收藏。";
                }
            }
            IsReady = true;
            Status = user is null ? "仅保存在本机" : State.Pending ? "本机修改等待同步" : "等待同步";
        }
        catch { Status = "无法读取收藏库，请检查本机文件或备份。"; throw; }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
        if (user.HasValue && _synchronize) _ = SyncAsync();
    }

    public async Task EditAsync(Action<LibraryGraph> edit, bool markLegacyImported = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsReady) throw new InvalidOperationException("收藏库尚未就绪。");
            var graph = new LibraryGraph(State.Document);
            edit(graph);
            var next = State with { Document = graph.Document, Pending = true,
                LegacyImported = State.LegacyImported || markLegacyImported };
            await WriteFileAsync(FilePath(next.UserId), next);
            State = next; _editVersion++;
            Status = HasConflict ? "存在同步冲突，本机修改已保存" : State.UserId.HasValue ? "本机修改已保存，等待同步" : "已保存在本机";
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
        ScheduleSync();
    }

    public async Task ImportLocalAsync()
    {
        if (!State.UserId.HasValue) return;
        var owner = State.UserId;
        var local = FilePath(null);
        if (!File.Exists(local)) return;
        var guest = await ReadFileAsync(local, null);
        // Merge through a fresh container, remapping IDs; never silently replace
        // the account library with a guest's document.
        await EditAsync(graph =>
        {
            if (State.UserId != owner) throw new OperationCanceledException("账号已切换，已取消导入。");
            var folder = graph.Create("folder", "本机导入 " + DateTime.Now.ToString("yyyy-MM-dd"));
            var media = new System.Collections.Generic.Dictionary<Guid, Guid>();
            foreach (var item in guest.Document.Media) media[item.Id] = graph.Remember(item with { Id = Guid.NewGuid() });
            var nodes = new System.Collections.Generic.Dictionary<Guid, Guid>();
            void CopyChildren(Guid? parent, Guid target)
            {
                foreach (var node in guest.Document.Nodes.FindAll(n => n.ParentId == parent))
                {
                    var id = graph.Create(node.Kind, node.Name, target); nodes[node.Id] = id;
                    if (node.Kind == "folder") CopyChildren(node.Id, id);
                }
            }
            CopyChildren(null, folder);
            foreach (var entry in System.Linq.Enumerable.OrderBy(guest.Document.Entries, e => e.Order))
                if (entry.MediaId is { } m) graph.AddMedia(nodes[entry.PlaylistId], new[] { media[m] });
                else if (entry.LinkedPlaylistId is { } link) graph.Link(nodes[entry.PlaylistId], nodes[link]);
            foreach (var like in guest.Document.Likes)
                if (!graph.Document.Likes.Exists(l => l.MediaId == media[like.MediaId])) graph.Like(media[like.MediaId], true);
        });
    }

    private void ScheduleSync()
    {
        if (_disposed || !_synchronize || !State.UserId.HasValue || HasConflict) return;
        _debounce?.Cancel();
        var cts = _debounce = new();
        _ = DebounceAsync(cts.Token);
    }
    private async Task DebounceAsync(CancellationToken ct)
    {
        try { await Task.Delay(700, ct); await SyncAsync(); }
        catch (OperationCanceledException) { }
    }

    public async Task SyncAsync(bool? keepLocal = null)
    {
        if (_disposed || !_synchronize || !IsReady || State.UserId is null || (HasConflict && keepLocal is null)) return;
        await _syncGate.WaitAsync();
        var generation = _generation; var ct = _profileCts.Token;
        try
        {
            if (!IsReady || State.UserId is null) return;
            Status = "正在同步…"; Changed?.Invoke(this, EventArgs.Empty);
            var owner = State.UserId.Value;
            var remote = await _cloud.ReadAsync(owner, ct);
            await _gate.WaitAsync(ct);
            LocalLibrary snapshot; long edits;
            try
            {
                if (generation != _generation) return;
                if (keepLocal is null && remote.Revision < State.CloudRevision)
                    throw new LibraryConflictException();
                // A lost HTTP response can leave a committed document marked
                // dirty locally. Recognize that commit instead of a false conflict.
                if (keepLocal is null && State.Pending && remote.Revision != State.CloudRevision &&
                    JsonSerializer.Serialize(remote.Document, LibraryJson.Options) == JsonSerializer.Serialize(State.Document, LibraryJson.Options))
                {
                    var acknowledged = State with { CloudRevision = remote.Revision, Pending = false };
                    await WriteFileAsync(FilePath(acknowledged.UserId), acknowledged); State = acknowledged;
                    HasConflict = false; Status = "已与云端同步"; return;
                }
                if (keepLocal == false || (!State.Pending && keepLocal is null))
                {
                    if (keepLocal == false) await WriteFileAsync(FilePath(State.UserId) + ".conflict-local", State);
                    var next = State with { CloudRevision = remote.Revision, Document = remote.Document, Pending = false };
                    await WriteFileAsync(FilePath(next.UserId), next); State = next; HasConflict = false;
                    Status = "已与云端同步"; return;
                }
                if (State.CloudRevision != remote.Revision && keepLocal != true) throw new LibraryConflictException();
                if (keepLocal == true)
                    await WriteFileAsync(FilePath(State.UserId) + ".conflict-cloud", new(State.UserId, remote.Revision, false, remote.Document));
                snapshot = State; edits = _editVersion;
            }
            finally { _gate.Release(); }
            var saved = await _cloud.WriteAsync(owner, remote.Revision, snapshot.Document, ct);
            await _gate.WaitAsync(ct);
            try
            {
                if (generation != _generation) return;
                var next = State with { CloudRevision = saved.Revision, Pending = _editVersion != edits };
                await WriteFileAsync(FilePath(next.UserId), next); State = next; HasConflict = false;
                Status = next.Pending ? "新的修改等待同步" : "已与云端同步";
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (LibraryConflictException ex) { if (generation == _generation) { HasConflict = true; Status = ex.Message; } }
        catch (InvalidOperationException ex) { if (generation == _generation) Status = ex.Message + " 本机修改已保留。"; }
        catch (Exception) { if (generation == _generation) Status = "暂时无法同步，本机收藏已保留。可稍后重试。"; }
        finally
        {
            _syncGate.Release();
            if (generation == _generation) { Changed?.Invoke(this, EventArgs.Empty); if (State.Pending && !HasConflict && Status == "新的修改等待同步") ScheduleSync(); }
        }
    }

    private static async Task<LocalLibrary> ReadFileAsync(string path, Guid? owner)
    {
        if (new FileInfo(path).Length > 20_000_000) throw new InvalidOperationException("收藏库文件过大。");
        var state = JsonSerializer.Deserialize<LocalLibrary>(await File.ReadAllTextAsync(path), LibraryJson.Options)
            ?? throw new InvalidOperationException("收藏库文件为空。");
        if (state.UserId != owner) throw new InvalidOperationException("收藏库账号不匹配。");
        LibraryGraph.Validate(state.Document);
        return state;
    }

    private static async Task WriteFileAsync(string path, LocalLibrary state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, LibraryJson.Options);
        await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        { await file.WriteAsync(bytes); await file.FlushAsync(); file.Flush(flushToDisk: true); }
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        File.Move(temporary, path, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounce?.Cancel(); _profileCts.Cancel();
        // In-flight continuations may still release the gates; do not dispose
        // their semaphores until the application has finished unwinding.
    }
}
