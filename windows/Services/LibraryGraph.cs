using System;
using System.Collections.Generic;
using System.Linq;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Services;

public sealed record LibraryOrderedItem(Guid Id, Guid? NodeId, Guid? MediaId, string Rank);

/// <summary>Deterministic command replay. The same rules apply offline and on the server.</summary>
public sealed class LibraryGraph
{
    public LibraryDocument Document { get; private set; }
    public List<LibraryCommand> Commands { get; } = new();
    public LibraryGraph(LibraryDocument document) { Validate(document); Document = document.Copy(); }
    public static IReadOnlyList<LibraryOrderedItem> Items(LibraryDocument d, Guid? parent) =>
        d.Nodes.Where(n => n.ParentId == parent).Select(n => new LibraryOrderedItem(n.Id, n.Id, null, n.Rank))
        .Concat(d.Entries.Where(e => e.PlaylistId == parent).Select(e => new LibraryOrderedItem(e.Id, null, e.MediaId, e.Rank)))
        .OrderBy(i => i.Rank, StringComparer.Ordinal).ThenBy(i => i.Id).ToArray();
    public IReadOnlyList<LibraryOrderedItem> Items(Guid? parent) => Items(Document, parent);
    private void Issue(LibraryCommand command)
    {
        var next = Replay(Document, command);
        Document = next;
        // Even a currently absent cancellation remains an intent for sync.
        Commands.Add(command);
    }
    public Guid Create(string kind, string name, Guid? parent = null)
    {
        var id = Guid.NewGuid();
        Issue(new() { Type = "create", NodeId = id, ParentId = parent, Name = name });
        return id;
    }
    public void Rename(Guid id, string name) => Issue(new() { Type = "rename", NodeId = id, Name = name });
    public void Move(Guid id, Guid? parent) => Issue(new() { Type = "move_node", NodeId = id, ParentId = parent });
    public void Delete(Guid id) => Issue(new() { Type = "delete", NodeId = id });
    public Guid Remember(LibraryMedia media)
    {
        var existing = Document.Media.FirstOrDefault(m => m.Bvid == media.Bvid && m.Cid == media.Cid);
        if (existing is not null) return existing.Id;
        Issue(new() { Type = "remember", Media = media }); return media.Id;
    }
    public void Like(Guid media, bool liked)
    {
        var track = Document.Media.FirstOrDefault(m => m.Id == media);
        if (track is null) return;
        Issue(new() { Type = "like", Bvid = track.Bvid, Cid = track.Cid, Liked = liked, OccurredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
    }
    public void SetLikeAt(Guid media, DateTimeOffset time)
    {
        var track = Document.Media.Single(m => m.Id == media);
        Issue(new() { Type = "like", Bvid = track.Bvid, Cid = track.Cid, Liked = true, OccurredAt = time.ToUnixTimeMilliseconds() });
    }
    public void AddMedia(Guid playlist, IEnumerable<Guid> media)
    {
        var tracks = media.Distinct().SelectMany(id => Document.Media.Where(m => m.Id == id))
            .Select(m => new LibraryTrackRef(m.Bvid, m.Cid, Guid.NewGuid())).ToList();
        foreach (var batch in tracks.Chunk(128)) Issue(new() { Type = "add_tracks", ParentId = playlist, Tracks = batch.ToList() });
    }
    public void RemoveEntry(Guid id)
    {
        var entry = Document.Entries.FirstOrDefault(e => e.Id == id);
        if (entry?.MediaId is not { } media) return;
        var track = Document.Media.Single(m => m.Id == media);
        Issue(new() { Type = "remove_track", ParentId = entry.PlaylistId, Bvid = track.Bvid, Cid = track.Cid });
    }
    public void RemoveMedia(Guid id)
    {
        var track = Document.Media.FirstOrDefault(m => m.Id == id);
        if (track is not null) Issue(new() { Type = "remove_media", Bvid = track.Bvid, Cid = track.Cid });
    }
    public void ReorderNode(Guid id, int delta) { if (Document.Nodes.FirstOrDefault(n => n.Id == id) is { } node) Reorder(id, node.ParentId, delta); }
    public void ReorderEntry(Guid id, int delta) { if (Document.Entries.FirstOrDefault(e => e.Id == id) is { } entry) Reorder(id, entry.PlaylistId, delta); }
    private void Reorder(Guid id, Guid? parent, int delta)
    {
        var items = Items(parent); var old = items.ToList().FindIndex(i => i.Id == id);
        var next = Math.Clamp(old + delta, 0, items.Count - 1);
        if (old == next) return;
        Issue(new() { Type = "reorder", ItemId = id, ParentId = parent, AnchorId = items[next].Id, Before = next < old });
    }
    public static LibraryDocument Replay(LibraryDocument document, LibraryCommand command, long? acceptedRevision = null)
    {
        var d = document.Copy();
        var c = command;
        bool ParentExists(Guid? p) => p is null || d.Nodes.Any(n => n.Id == p);
        LibraryMedia? FindTrack() => d.Media.FirstOrDefault(m => m.Bvid == c.Bvid && m.Cid == c.Cid);
        string Append(Guid? p) => FractionalRank.Between(Items(d, p).LastOrDefault()?.Rank, null);
        switch (c.Type)
        {
            case "remember":
                var m = c.Media ?? throw new InvalidOperationException("Missing media.");
                if (!d.Media.Any(x => x.Bvid == m.Bvid && x.Cid == m.Cid) && !d.Media.Any(x => x.Id == m.Id)) d.Media.Add(m);
                break;
            case "create":
                if (c.NodeId is not { } id || !ParentExists(c.ParentId) || d.Nodes.Any(n => n.Id == id) || d.Entries.Any(e => e.Id == id)) break;
                if (Depth(c.ParentId) >= 64) break;
                d.Nodes.Add(new(id, "playlist", c.Name?.Trim() ?? "", c.ParentId, Append(c.ParentId))); break;
            case "rename":
                var rename = d.Nodes.FindIndex(n => n.Id == c.NodeId);
                if (rename >= 0) d.Nodes[rename] = d.Nodes[rename] with { Name = c.Name?.Trim() ?? "" }; break;
            case "move_node":
                var moving = d.Nodes.FindIndex(n => n.Id == c.NodeId);
                if (moving < 0 || !ParentExists(c.ParentId)) break;
                for (var parent = c.ParentId; parent.HasValue; parent = d.Nodes.Single(n => n.Id == parent).ParentId)
                    if (parent == c.NodeId) return document;
                if (Depth(c.ParentId) + Height(c.NodeId!.Value) > 64) break;
                d.Nodes[moving] = d.Nodes[moving] with { ParentId = c.ParentId, Rank = Append(c.ParentId) }; break;
            case "delete":
                var removed = new HashSet<Guid>();
                if (c.NodeId is not { } target || !d.Nodes.Any(n => n.Id == target)) break;
                var pending = new Stack<Guid>(); pending.Push(target);
                while (pending.TryPop(out var child)) { removed.Add(child); foreach (var n in d.Nodes.Where(n => n.ParentId == child)) pending.Push(n.Id); }
                d.Nodes.RemoveAll(n => removed.Contains(n.Id)); d.Entries.RemoveAll(e => removed.Contains(e.PlaylistId)); break;
            case "like":
                var likedTrack = FindTrack(); if (likedTrack is null) break;
                if (!c.Liked) d.Likes.RemoveAll(l => l.MediaId == likedTrack.Id);
                else if (!d.Likes.Any(l => l.MediaId == likedTrack.Id))
                    d.Likes.Add(new(likedTrack.Id, DateTimeOffset.FromUnixTimeMilliseconds(c.OccurredAt),
                        acceptedRevision ?? d.Likes.Select(l => l.Sequence).DefaultIfEmpty(0).Max() + 1));
                break;
            case "add_tracks":
                if (c.ParentId is not { } into || !ParentExists(into)) break;
                foreach (var t in c.Tracks)
                {
                    var track = d.Media.FirstOrDefault(x => x.Bvid == t.Bvid && x.Cid == t.Cid);
                    if (track is not null && !d.Entries.Any(e => e.PlaylistId == into && e.MediaId == track.Id) &&
                        !d.Entries.Any(e => e.Id == t.EntryId) && !d.Nodes.Any(n => n.Id == t.EntryId))
                        d.Entries.Add(new(t.EntryId, into, track.Id, Append(into)));
                }
                break;
            case "remove_track":
                var unlinked = FindTrack();
                if (unlinked is not null) d.Entries.RemoveAll(e => e.PlaylistId == c.ParentId && e.MediaId == unlinked.Id); break;
            case "remove_media":
                var deleted = FindTrack();
                if (deleted is not null) { d.Likes.RemoveAll(l => l.MediaId == deleted.Id); d.Entries.RemoveAll(e => e.MediaId == deleted.Id); d.Media.Remove(deleted); }
                break;
            case "reorder":
                var siblings = Items(d, c.ParentId).Where(i => i.Id != c.ItemId).ToList();
                var current = Items(d, c.ParentId).FirstOrDefault(i => i.Id == c.ItemId);
                var anchor = siblings.FindIndex(i => i.Id == c.AnchorId);
                if (current is null || anchor < 0) break;
                var slot = anchor + (c.Before ? 0 : 1);
                var rank = FractionalRank.Between(slot > 0 ? siblings[slot - 1].Rank : null, slot < siblings.Count ? siblings[slot].Rank : null);
                var ni = d.Nodes.FindIndex(n => n.Id == current.Id);
                if (ni >= 0) d.Nodes[ni] = d.Nodes[ni] with { Rank = rank };
                else { var ei = d.Entries.FindIndex(e => e.Id == current.Id); d.Entries[ei] = d.Entries[ei] with { Rank = rank }; }
                break;
            default: throw new InvalidOperationException("Unknown library operation.");
        }
        RebalanceLongRanks(d);
        Validate(d); return d;
        int Depth(Guid? id) { var depth = 0; for (var p = id; p.HasValue; p = d.Nodes.Single(n => n.Id == p).ParentId) depth++; return depth; }
        int Height(Guid id) => 1 + d.Nodes.Where(n => n.ParentId == id).Select(n => Height(n.Id)).DefaultIfEmpty(0).Max();
    }
    private static void RebalanceLongRanks(LibraryDocument d)
    {
        var parents = d.Nodes.Where(n => n.Rank.Length > 256).Select(n => n.ParentId)
            .Concat(d.Entries.Where(e => e.Rank.Length > 256).Select(e => (Guid?)e.PlaylistId)).Distinct().ToArray();
        foreach (var p in parents)
        {
            string? rank = null;
            foreach (var item in Items(d, p))
            {
                rank = FractionalRank.Between(rank, null);
                if (item.NodeId.HasValue) { var i = d.Nodes.FindIndex(n => n.Id == item.Id); d.Nodes[i] = d.Nodes[i] with { Rank = rank }; }
                else { var i = d.Entries.FindIndex(e => e.Id == item.Id); d.Entries[i] = d.Entries[i] with { Rank = rank }; }
            }
        }
    }
    public IReadOnlyList<LibraryMedia> Expand(Guid? node, bool likes = false) => Resolve(node, likes).Select(r => r.Media).ToArray();
    public IReadOnlyList<ResolvedLibraryMedia> Resolve(Guid? node, bool likes = false)
    {
        var result = new List<ResolvedLibraryMedia>(); var seen = new HashSet<Guid>(); var media = Document.Media.ToDictionary(m => m.Id);
        var children = Document.Nodes.Select(n => (Parent: n.ParentId ?? LibrarySqliteStore.Root, Item: new LibraryOrderedItem(n.Id, n.Id, null, n.Rank)))
            .Concat(Document.Entries.Select(e => (Parent: e.PlaylistId, Item: new LibraryOrderedItem(e.Id, null, e.MediaId, e.Rank))))
            .GroupBy(i => i.Parent).ToDictionary(g => g.Key, g => g.Select(i => i.Item).OrderBy(i => i.Rank, StringComparer.Ordinal).ToArray());
        void Add(Guid id, Guid? p = null, Guid? entry = null) { if (seen.Add(id)) result.Add(new(media[id], p, entry)); }
        void Visit(Guid id)
        {
            foreach (var item in children.GetValueOrDefault(id) ?? Array.Empty<LibraryOrderedItem>())
                if (item.NodeId is { } child) Visit(child); else if (item.MediaId is { } m) Add(m, id, item.Id);
        }
        if (likes) foreach (var l in Document.Likes.OrderByDescending(l => l.AddedAt).ThenByDescending(l => l.Sequence).ThenBy(l => l.MediaId)) Add(l.MediaId);
        else if (node is { } id) Visit(id);
        else foreach (var item in Document.Media) Add(item.Id);
        return result;
    }
    public static void Validate(LibraryDocument d)
    {
        if (d.Version != 2 || d.Media.Count > 100000 || d.Nodes.Count > 5000 || d.Entries.Count > 300000) throw new InvalidOperationException("收藏库版本或大小不受支持。");
        static void Unique(IEnumerable<Guid> ids) { var set = new HashSet<Guid>(); foreach (var id in ids) if (id == Guid.Empty || id == LibrarySqliteStore.Root || id == LibrarySqliteStore.Default || !set.Add(id)) throw new InvalidOperationException("无效或重复的 ID。"); }
        Unique(d.Media.Select(m => m.Id)); Unique(d.Nodes.Select(n => n.Id).Concat(d.Entries.Select(e => e.Id))); Unique(d.Likes.Select(l => l.MediaId));
        var nodes = d.Nodes.ToDictionary(n => n.Id); var media = d.Media.Select(m => m.Id).ToHashSet();
        if (d.Media.Any(m => string.IsNullOrWhiteSpace(m.Bvid) || m.Bvid.Length > 64 || m.Cid < 0 || m.DurationSeconds < 0 || m.Title.Length > 10000 || m.Artist.Length > 10000 || m.Cover.Length > 10000 || m.Duration.Length > 120) || d.Media.GroupBy(m => (m.Bvid, m.Cid)).Any(g => g.Count() > 1)) throw new InvalidOperationException("音源无效或重复。");
        foreach (var n in d.Nodes)
        {
            if (n.Kind != "playlist" || string.IsNullOrWhiteSpace(n.Name) || n.Name.Length > 120 || (n.ParentId is { } p && !nodes.ContainsKey(p))) throw new InvalidOperationException("歌单名称或父级无效。");
            FractionalRank.Validate(n.Rank);
            var visited = new HashSet<Guid> { n.Id }; var parent = n.ParentId;
            while (parent.HasValue) { if (visited.Count >= 64 || !visited.Add(parent.Value)) throw new InvalidOperationException("歌单循环或嵌套过深。"); parent = nodes[parent.Value].ParentId; }
        }
        foreach (var e in d.Entries)
        { if (!nodes.ContainsKey(e.PlaylistId) || e.MediaId is not { } m || !media.Contains(m)) throw new InvalidOperationException("歌曲条目无效。"); FractionalRank.Validate(e.Rank); }
        if (d.Likes.Any(l => !media.Contains(l.MediaId) || l.Sequence < 0) || d.Entries.GroupBy(e => (e.PlaylistId, e.MediaId)).Any(g => g.Count() > 1)) throw new InvalidOperationException("收藏关系重复或无效。");
        if (d.Nodes.Select(n => (n.ParentId, n.Rank)).Concat(d.Entries.Select(e => ((Guid?)e.PlaylistId, e.Rank)))
            .GroupBy(i => i).Any(g => g.Count() > 1)) throw new InvalidOperationException("排序键重复。");
    }
}
