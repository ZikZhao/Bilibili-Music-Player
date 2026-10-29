using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using bilibili_music_player_windows.Models;
using Microsoft.Data.Sqlite;

namespace bilibili_music_player_windows.Services;

public sealed record StoredLibrary(Guid? Owner, long Revision, string Hash, LibraryDocument Baseline,
    LibraryDocument Document, Guid Writer, long NextSequence, List<LibraryCommand> Pending, bool LegacyImported);

/// <summary>Relational base/work layers and an atomic durable outbox. JSON is only
/// used for individual command payloads, never for the whole library state.</summary>
public sealed class LibrarySqliteStore : IDisposable
{
    public static readonly Guid Root = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid Default = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private readonly SqliteConnection _db;
    private SqliteTransaction? _transaction;
    private static string Profile(Guid? owner) => owner?.ToString("D") ?? "local";
    public LibrarySqliteStore(string root)
    {
        Directory.CreateDirectory(root);
        _db = new(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "library.db"), Pooling = false }.ToString());
        _db.Open();
        Run("PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;");
        Run("""
            CREATE TABLE IF NOT EXISTS profiles(profile TEXT PRIMARY KEY,revision INTEGER NOT NULL,hash TEXT NOT NULL,writer TEXT NOT NULL,next_sequence INTEGER NOT NULL,legacy_imported INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS media(profile TEXT NOT NULL,layer TEXT NOT NULL,id TEXT NOT NULL,bvid TEXT NOT NULL,cid INTEGER NOT NULL,title TEXT NOT NULL,artist TEXT NOT NULL,cover TEXT NOT NULL,duration_seconds INTEGER NOT NULL,duration TEXT NOT NULL,
              PRIMARY KEY(profile,layer,id), UNIQUE(profile,layer,bvid,cid));
            CREATE TABLE IF NOT EXISTS playlists(profile TEXT NOT NULL,layer TEXT NOT NULL,id TEXT NOT NULL,name TEXT NOT NULL,kind TEXT NOT NULL,PRIMARY KEY(profile,layer,id));
            CREATE TABLE IF NOT EXISTS playlist_items(profile TEXT NOT NULL,layer TEXT NOT NULL,parent_id TEXT NOT NULL,id TEXT NOT NULL,media_id TEXT,child_id TEXT,rank TEXT COLLATE BINARY NOT NULL,added_at_ms INTEGER,sequence INTEGER NOT NULL DEFAULT 0,
              PRIMARY KEY(profile,layer,parent_id,id),CHECK((media_id IS NULL)!=(child_id IS NULL)),
              FOREIGN KEY(profile,layer,parent_id) REFERENCES playlists(profile,layer,id),
              FOREIGN KEY(profile,layer,child_id) REFERENCES playlists(profile,layer,id),
              FOREIGN KEY(profile,layer,media_id) REFERENCES media(profile,layer,id));
            CREATE UNIQUE INDEX IF NOT EXISTS single_parent ON playlist_items(profile,layer,child_id) WHERE child_id IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS single_song ON playlist_items(profile,layer,parent_id,media_id) WHERE media_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS item_order ON playlist_items(profile,layer,parent_id,rank);
            CREATE TABLE IF NOT EXISTS pending_operations(profile TEXT NOT NULL,sequence INTEGER NOT NULL,id TEXT NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(profile,sequence),UNIQUE(profile,id));
            PRAGMA user_version=2;
            """);
    }
    private void Run(string sql, params object?[] values)
    {
        using var command = _db.CreateCommand(); command.CommandText = sql; command.Transaction = _transaction;
        for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("@p" + i, values[i] ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
    public StoredLibrary? Read(Guid? owner)
    {
        var p = Profile(owner);
        using var cmd = _db.CreateCommand(); cmd.CommandText = "SELECT revision,hash,writer,next_sequence,legacy_imported FROM profiles WHERE profile=@p"; cmd.Parameters.AddWithValue("@p", p);
        using var reader = cmd.ExecuteReader(); if (!reader.Read()) return null;
        var revision = reader.GetInt64(0); var hash = reader.GetString(1); var writer = Guid.Parse(reader.GetString(2)); var seq = reader.GetInt64(3); var legacy = reader.GetBoolean(4); reader.Close();
        var baseline = ReadLayer(p, "base");
        if (LibraryProtocol.Hash(baseline) != hash) throw new InvalidDataException("本机云端基线校验失败。");
        var commands = new List<LibraryCommand>();
        using var q = _db.CreateCommand(); q.CommandText = "SELECT payload FROM pending_operations WHERE profile=@p ORDER BY sequence"; q.Parameters.AddWithValue("@p", p);
        using var r = q.ExecuteReader(); while (r.Read()) commands.Add(JsonSerializer.Deserialize<LibraryCommand>(r.GetString(0), LibraryJson.Options)!); r.Close();
        // Rebuild from the validated base and durable intent, not editable work rows.
        var document = baseline;
        foreach (var operation in commands) document = LibraryGraph.Replay(document, operation);
        return new(owner, revision, hash, baseline, document, writer, seq, commands, legacy);
    }
    public StoredLibrary Recover(Guid owner)
    {
        // Preserve the evidence and durable intents; never upload edited base/work rows.
        var backupPath = _db.DataSource + ".unverified-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        using (var backup = new SqliteConnection("Data Source=" + backupPath)) { backup.Open(); _db.BackupDatabase(backup); }
        var p = Profile(owner); Guid writer; long next; bool legacy;
        using (var c = _db.CreateCommand())
        {
            c.CommandText = "SELECT writer,next_sequence,legacy_imported FROM profiles WHERE profile=@p"; c.Parameters.AddWithValue("@p", p);
            using var r = c.ExecuteReader(); if (!r.Read()) throw new InvalidDataException("缺少本机同步身份。");
            writer = Guid.Parse(r.GetString(0)); next = r.GetInt64(1); legacy = r.GetBoolean(2);
        }
        var pending = new List<LibraryCommand>();
        using (var c = _db.CreateCommand())
        {
            c.CommandText = "SELECT payload FROM pending_operations WHERE profile=@p ORDER BY sequence"; c.Parameters.AddWithValue("@p", p);
            using var r = c.ExecuteReader(); while (r.Read()) pending.Add(JsonSerializer.Deserialize<LibraryCommand>(r.GetString(0), LibraryJson.Options)!);
        }
        var empty = new LibraryDocument(); var state = new StoredLibrary(owner, 0, LibraryProtocol.Hash(empty), empty, empty, writer, next, pending, legacy);
        using (var tx = _db.BeginTransaction())
        {
            _transaction = tx;
            try { Run("DELETE FROM playlist_items WHERE profile=@p0", p); Run("DELETE FROM playlists WHERE profile=@p0", p); Run("DELETE FROM media WHERE profile=@p0", p); tx.Commit(); }
            finally { _transaction = null; }
        }
        Save(state); return state;
    }
    private LibraryDocument ReadLayer(string profile, string layer)
    {
        var d = new LibraryDocument();
        using var c = _db.CreateCommand(); c.Transaction = _transaction;
        c.Parameters.AddWithValue("@p", profile); c.Parameters.AddWithValue("@l", layer);
        c.CommandText = "SELECT id,bvid,cid,title,artist,cover,duration_seconds,duration FROM media WHERE profile=@p AND layer=@l ORDER BY id";
        using (var r = c.ExecuteReader()) while (r.Read()) d.Media.Add(new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt64(6), r.GetString(7)));
        c.CommandText = "SELECT p.id,p.name,i.parent_id,i.rank FROM playlists p JOIN playlist_items i ON i.profile=p.profile AND i.layer=p.layer AND i.child_id=p.id WHERE p.profile=@p AND p.layer=@l AND p.kind='playlist' ORDER BY p.id";
        using (var r = c.ExecuteReader()) while (r.Read()) d.Nodes.Add(new(Guid.Parse(r.GetString(0)), "playlist", r.GetString(1), Guid.Parse(r.GetString(2)) == Root ? null : Guid.Parse(r.GetString(2)), r.GetString(3)));
        c.CommandText = "SELECT id,parent_id,media_id,rank,added_at_ms,sequence FROM playlist_items WHERE profile=@p AND layer=@l AND media_id IS NOT NULL ORDER BY id";
        using (var r = c.ExecuteReader()) while (r.Read())
        {
            var parent = Guid.Parse(r.GetString(1)); var media = Guid.Parse(r.GetString(2));
            if (parent == Default) d.Likes.Add(new(media, DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(4)), r.GetInt64(5)));
            else d.Entries.Add(new(Guid.Parse(r.GetString(0)), parent, media, r.GetString(3)));
        }
        LibraryGraph.Validate(d); return d;
    }
    public void Save(StoredLibrary state)
    {
        var p = Profile(state.Owner);
        using var transaction = _db.BeginTransaction();
        _transaction = transaction;
        // All commands on this connection enlist in its current transaction.
        try
        {
            WriteLayer(p, "base", state.Baseline); WriteLayer(p, "work", state.Document);
            Run("INSERT INTO profiles VALUES(@p0,@p1,@p2,@p3,@p4,@p5) ON CONFLICT(profile) DO UPDATE SET revision=excluded.revision,hash=excluded.hash,writer=excluded.writer,next_sequence=excluded.next_sequence,legacy_imported=excluded.legacy_imported", p, state.Revision, state.Hash, state.Writer.ToString("D"), state.NextSequence, state.LegacyImported);
            var keep = state.Pending.Select(c => c.Sequence).ToHashSet();
            using var q = _db.CreateCommand(); q.Transaction = transaction; q.CommandText = "SELECT sequence FROM pending_operations WHERE profile=@p"; q.Parameters.AddWithValue("@p", p);
            var removed = new List<long>(); using (var r = q.ExecuteReader()) while (r.Read()) if (!keep.Contains(r.GetInt64(0))) removed.Add(r.GetInt64(0));
            foreach (var sequence in removed) Run("DELETE FROM pending_operations WHERE profile=@p0 AND sequence=@p1", p, sequence);
            foreach (var operation in state.Pending) Run("INSERT INTO pending_operations VALUES(@p0,@p1,@p2,@p3) ON CONFLICT(profile,sequence) DO NOTHING", p, operation.Sequence, operation.Id.ToString("D"), JsonSerializer.Serialize(operation, LibraryJson.Options));
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
        finally { _transaction = null; }
    }
    private void WriteLayer(string profile, string layer, LibraryDocument next)
    {
        LibraryDocument old;
        try { old = ReadLayer(profile, layer); }
        catch (Exception ex) when (layer == "work" && ex is InvalidOperationException or ArgumentException or FormatException)
        {
            Run("DELETE FROM playlist_items WHERE profile=@p0 AND layer=@p1", profile, layer);
            Run("DELETE FROM playlists WHERE profile=@p0 AND layer=@p1", profile, layer);
            Run("DELETE FROM media WHERE profile=@p0 AND layer=@p1", profile, layer);
            old = new();
        }
        var diff = LibraryProtocol.Diff(old, next);
        // Delete relationship rows first, including moved child memberships.
        foreach (var id in diff.RemovedNodes.Concat(diff.Nodes.Select(n => n.Id))) Run("DELETE FROM playlist_items WHERE profile=@p0 AND layer=@p1 AND child_id=@p2", profile, layer, id.ToString("D"));
        foreach (var id in diff.RemovedEntries.Concat(diff.Entries.Select(e => e.Id))) Run("DELETE FROM playlist_items WHERE profile=@p0 AND layer=@p1 AND id=@p2 AND child_id IS NULL AND parent_id<>@p3", profile, layer, id.ToString("D"), Default.ToString("D"));
        foreach (var id in diff.RemovedLikes) Run("DELETE FROM playlist_items WHERE profile=@p0 AND layer=@p1 AND parent_id=@p2 AND media_id=@p3", profile, layer, Default.ToString("D"), id.ToString("D"));
        foreach (var id in diff.RemovedNodes) Run("DELETE FROM playlists WHERE profile=@p0 AND layer=@p1 AND id=@p2", profile, layer, id.ToString("D"));
        foreach (var id in diff.RemovedMedia) Run("DELETE FROM media WHERE profile=@p0 AND layer=@p1 AND id=@p2", profile, layer, id.ToString("D"));
        foreach (var m in diff.Media) Run("INSERT INTO media VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9) ON CONFLICT(profile,layer,id) DO UPDATE SET bvid=excluded.bvid,cid=excluded.cid,title=excluded.title,artist=excluded.artist,cover=excluded.cover,duration_seconds=excluded.duration_seconds,duration=excluded.duration", profile, layer, m.Id.ToString("D"), m.Bvid, m.Cid, m.Title, m.Artist, m.Cover, m.DurationSeconds, m.Duration);
        Run("INSERT OR IGNORE INTO playlists VALUES(@p0,@p1,@p2,'收藏库','root')", profile, layer, Root.ToString("D"));
        Run("INSERT OR IGNORE INTO playlists VALUES(@p0,@p1,@p2,'我喜欢','default')", profile, layer, Default.ToString("D"));
        foreach (var n in diff.Nodes) Run("INSERT INTO playlists VALUES(@p0,@p1,@p2,@p3,'playlist') ON CONFLICT(profile,layer,id) DO UPDATE SET name=excluded.name", profile, layer, n.Id.ToString("D"), n.Name);
        foreach (var n in diff.Nodes) Item(profile, layer, n.ParentId ?? Root, n.Id, null, n.Id, n.Rank, null, 0);
        foreach (var e in diff.Entries) Item(profile, layer, e.PlaylistId, e.Id, e.MediaId, null, e.Rank, null, 0);
        foreach (var l in diff.Likes) Item(profile, layer, Default, l.MediaId, l.MediaId, null, "", l.AddedAt.ToUnixTimeMilliseconds(), l.Sequence);
    }
    private void Item(string profile, string layer, Guid parent, Guid id, Guid? media, Guid? child, string rank, long? time, long sequence)
        => Run("INSERT INTO playlist_items VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8) ON CONFLICT(profile,layer,parent_id,id) DO UPDATE SET media_id=excluded.media_id,child_id=excluded.child_id,rank=excluded.rank,added_at_ms=excluded.added_at_ms,sequence=excluded.sequence", profile, layer, parent.ToString("D"), id.ToString("D"), media?.ToString("D"), child?.ToString("D"), rank, time, sequence);
    public void Dispose() => _db.Dispose();
}
