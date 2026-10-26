using System;
using System.Collections.Generic;
using System.Linq;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Services;

/// <summary>Library operations shared by local storage, UI, synchronization and tests.</summary>
public sealed class LibraryGraph
{
    public LibraryDocument Document { get; private set; }
    public LibraryGraph(LibraryDocument document) { Validate(document); Document = document.Copy(); }

    private void Change(Action<LibraryDocument> edit)
    {
        var next = Document.Copy();
        edit(next);
        Validate(next);
        Document = next;
    }

    public Guid Create(string kind, string name, Guid? parent = null)
    {
        var id = Guid.NewGuid();
        Change(d => d.Nodes.Add(new(id, kind, name.Trim(), parent, NextOrder(d.Nodes.Where(n => n.ParentId == parent).Select(n => n.Order)))));
        return id;
    }

    public void Rename(Guid id, string name) => Change(d =>
    {
        var index = d.Nodes.FindIndex(n => n.Id == id);
        if (index < 0) throw new InvalidOperationException("这个项目已经不存在。");
        d.Nodes[index] = d.Nodes[index] with { Name = name.Trim() };
    });

    public void Move(Guid id, Guid? parent) => Change(d =>
    {
        var index = d.Nodes.FindIndex(n => n.Id == id);
        if (index < 0) throw new InvalidOperationException("这个项目已经不存在。");
        d.Nodes[index] = d.Nodes[index] with { ParentId = parent,
            Order = NextOrder(d.Nodes.Where(n => n.ParentId == parent).Select(n => n.Order)) };
    });

    public void Delete(Guid id) => Change(d =>
    {
        var node = d.Nodes.Single(n => n.Id == id);
        // Dissolve a folder, preserving its children. Deleting a playlist removes
        // its incoming links, never the underlying media or other playlists.
        var order = NextOrder(d.Nodes.Where(n => n.ParentId == node.ParentId).Select(n => n.Order));
        foreach (var child in d.Nodes.Where(n => n.ParentId == id).OrderBy(n => n.Order).ToArray())
        {
            var index = d.Nodes.IndexOf(child);
            d.Nodes[index] = child with { ParentId = node.ParentId, Order = order++ };
        }
        d.Nodes.Remove(node);
        d.Entries.RemoveAll(e => e.PlaylistId == id || e.LinkedPlaylistId == id);
    });

    public Guid Remember(LibraryMedia media)
    {
        var existing = Document.Media.FirstOrDefault(m => m.Bvid == media.Bvid && m.Cid == media.Cid);
        if (existing is not null) return existing.Id;
        Change(d => d.Media.Add(media));
        return media.Id;
    }

    public void Like(Guid media, bool liked) => Change(d =>
    {
        d.Likes.RemoveAll(l => l.MediaId == media);
        if (liked) d.Likes.Add(new(media, DateTimeOffset.UtcNow));
    });

    public void AddMedia(Guid playlist, IEnumerable<Guid> media)
    {
        Change(d =>
        {
            var order = NextOrder(d.Entries.Where(e => e.PlaylistId == playlist).Select(e => e.Order));
            foreach (var id in media.Distinct())
                if (!d.Entries.Any(e => e.PlaylistId == playlist && e.MediaId == id))
                    d.Entries.Add(new(Guid.NewGuid(), playlist, id, null, order++));
        });
    }

    public void Link(Guid playlist, Guid target) => Change(d =>
    {
        if (!d.Entries.Any(e => e.PlaylistId == playlist && e.LinkedPlaylistId == target))
            d.Entries.Add(new(Guid.NewGuid(), playlist, null, target,
                NextOrder(d.Entries.Where(e => e.PlaylistId == playlist).Select(e => e.Order))));
    });

    public void RemoveEntry(Guid id) => Change(d => d.Entries.RemoveAll(e => e.Id == id));

    public void RemoveMedia(Guid id) => Change(d =>
    {
        d.Entries.RemoveAll(e => e.MediaId == id);
        d.Likes.RemoveAll(l => l.MediaId == id);
        d.Media.RemoveAll(m => m.Id == id);
    });

    public void ReorderNode(Guid id, int delta) => Change(d =>
    {
        var node = d.Nodes.Single(n => n.Id == id);
        var list = d.Nodes.Where(n => n.ParentId == node.ParentId).OrderBy(n => n.Order).ThenBy(n => n.Id).ToList();
        var old = list.IndexOf(node); var next = Math.Clamp(old + delta, 0, list.Count - 1);
        list.RemoveAt(old); list.Insert(next, node);
        for (var i = 0; i < list.Count; i++) d.Nodes[d.Nodes.FindIndex(n => n.Id == list[i].Id)] = list[i] with { Order = i };
    });

    public void ReorderEntry(Guid id, int delta) => Change(d =>
    {
        var entry = d.Entries.Single(e => e.Id == id);
        var list = d.Entries.Where(e => e.PlaylistId == entry.PlaylistId).OrderBy(e => e.Order).ThenBy(e => e.Id).ToList();
        var old = list.FindIndex(e => e.Id == id);
        var next = Math.Clamp(old + delta, 0, list.Count - 1);
        list.RemoveAt(old); list.Insert(next, entry);
        for (var i = 0; i < list.Count; i++)
            d.Entries[d.Entries.FindIndex(e => e.Id == list[i].Id)] = list[i] with { Order = i };
    });

    public IReadOnlyList<LibraryMedia> Expand(Guid? node, bool likes = false) => Resolve(node, likes).Select(r => r.Media).ToArray();

    public IReadOnlyList<ResolvedLibraryMedia> Resolve(Guid? node, bool likes = false)
    {
        var result = new List<ResolvedLibraryMedia>();
        var seen = new HashSet<Guid>();
        var expanded = new HashSet<Guid>();
        var media = Document.Media.ToDictionary(m => m.Id);
        void Add(Guid id, Guid? playlist = null, Guid? entry = null)
        { if (seen.Add(id) && media.TryGetValue(id, out var item)) result.Add(new(item, playlist, entry)); }
        void Visit(Guid id)
        {
            if (!expanded.Add(id)) return; // Shared subgraphs expand once; first occurrence wins.
            var item = Document.Nodes.Single(n => n.Id == id);
            if (item.Kind == "folder")
                foreach (var child in Document.Nodes.Where(n => n.ParentId == id).OrderBy(n => n.Order).ThenBy(n => n.Id)) Visit(child.Id);
            else
                foreach (var entry in Document.Entries.Where(e => e.PlaylistId == id).OrderBy(e => e.Order).ThenBy(e => e.Id))
                    if (entry.MediaId is { } m) Add(m, id, entry.Id); else if (entry.LinkedPlaylistId is { } link) Visit(link);
        }
        if (likes) foreach (var like in Document.Likes.OrderByDescending(l => l.AddedAt)) Add(like.MediaId);
        else if (node is { } id) Visit(id);
        else foreach (var item in Document.Media) Add(item.Id);
        return result;
    }

    private static long NextOrder(IEnumerable<long> values) => values.DefaultIfEmpty(-1).Max() + 1;

    public static void Validate(LibraryDocument d)
    {
        if (d is null || d.Nodes is null || d.Media is null || d.Entries is null || d.Likes is null ||
            d.Version != 1 || d.Nodes.Count > 5000 || d.Media.Count > 100000 || d.Entries.Count > 300000)
            throw new InvalidOperationException("收藏库版本或大小不受支持。");
        if (d.Nodes.Any(n => n is null) || d.Media.Any(m => m is null) || d.Entries.Any(e => e is null) || d.Likes.Any(l => l is null))
            throw new InvalidOperationException("收藏库包含空条目。");
        var nodes = Unique(d.Nodes, n => n.Id); var media = Unique(d.Media, m => m.Id);
        Unique(d.Entries, e => e.Id); Unique(d.Likes, l => l.MediaId);
        if (d.Media.Any(m => string.IsNullOrWhiteSpace(m.Bvid) || m.Cid < 0 || m.DurationSeconds < 0) ||
            d.Media.GroupBy(m => (m.Bvid, m.Cid)).Any(g => g.Count() > 1)) throw new InvalidOperationException("音源条目无效或重复。");
        foreach (var node in d.Nodes)
        {
            if (node.Kind is not ("folder" or "playlist") || string.IsNullOrWhiteSpace(node.Name) || node.Name.Length > 120)
                throw new InvalidOperationException("名称应为 1–120 个字符。");
            if (node.ParentId is { } parent && (!nodes.TryGetValue(parent, out var p) || p.Kind != "folder"))
                throw new InvalidOperationException("只能将项目放入文件夹。");
        }
        foreach (var e in d.Entries)
        {
            if (!nodes.TryGetValue(e.PlaylistId, out var p) || p.Kind != "playlist" ||
                (e.MediaId.HasValue == e.LinkedPlaylistId.HasValue) ||
                (e.MediaId is { } m && !media.ContainsKey(m)) ||
                (e.LinkedPlaylistId is { } link && (!nodes.TryGetValue(link, out var target) || target.Kind != "playlist")))
                throw new InvalidOperationException("歌单条目或动态引用无效。");
        }
        if (d.Likes.Any(l => !media.ContainsKey(l.MediaId))) throw new InvalidOperationException("喜欢的音源不存在。");
        if (d.Entries.GroupBy(e => (e.PlaylistId, e.MediaId, e.LinkedPlaylistId)).Any(g => g.Count() > 1))
            throw new InvalidOperationException("歌单包含重复条目。");
        CheckCycles(d.Nodes.Select(n => n.Id), id => nodes[id].ParentId is { } p ? new[] { p } : Array.Empty<Guid>());
        var links = d.Entries.Where(e => e.LinkedPlaylistId.HasValue).ToLookup(e => e.PlaylistId, e => e.LinkedPlaylistId!.Value);
        CheckCycles(d.Nodes.Where(n => n.Kind == "playlist").Select(n => n.Id), id => links[id]);
    }

    private static Dictionary<Guid, T> Unique<T>(IEnumerable<T> items, Func<T, Guid> key)
    {
        var result = new Dictionary<Guid, T>();
        foreach (var item in items) if (key(item) == Guid.Empty || !result.TryAdd(key(item), item))
            throw new InvalidOperationException("收藏库包含无效或重复的 ID。");
        return result;
    }

    private static void CheckCycles(IEnumerable<Guid> ids, Func<Guid, IEnumerable<Guid>> children)
    {
        var heights = new Dictionary<Guid, int>(); var visiting = new HashSet<Guid>();
        int Visit(Guid id, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("嵌套层级不能超过 64 层。");
            if (heights.TryGetValue(id, out var cached)) return cached;
            if (!visiting.Add(id)) throw new InvalidOperationException("这个操作会形成循环引用，请选择其他位置或歌单。");
            var height = 0;
            foreach (var child in children(id)) height = Math.Max(height, 1 + Visit(child, depth + 1));
            if (height > 64) throw new InvalidOperationException("嵌套层级不能超过 64 层。");
            visiting.Remove(id); heights[id] = height; return height;
        }
        foreach (var id in ids) Visit(id, 0);
    }
}
