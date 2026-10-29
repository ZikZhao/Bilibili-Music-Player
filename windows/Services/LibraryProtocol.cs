using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Services;

public static class LibraryProtocol
{
    public static LibraryDocument Upgrade(LibraryDocument source)
    {
        if (source.Version == 2) { LibraryGraph.Validate(source); return source; }
        if (source.Version != 1 || source.Nodes.Count != 0 || source.Entries.Count != 0)
            throw new InvalidOperationException("一次性转换只接受当前开发版的收藏数据。");
        var d = new LibraryDocument { Media = new(source.Media) };
        d.Likes.AddRange(source.Likes.Select((l, i) => l with { Sequence = i + 1 }));
        LibraryGraph.Validate(d); return d;
    }
    public static string Hash(LibraryDocument d)
    {
        static string B(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? ""));
        static string G(Guid? id) => id?.ToString("D") ?? "-";
        var s = new StringBuilder("library-v2\n");
        foreach (var m in d.Media.OrderBy(m => m.Id.ToString("D"), StringComparer.Ordinal))
            s.Append(CultureInfo.InvariantCulture, $"m|{G(m.Id)}|{B(m.Bvid)}|{m.Cid}|{B(m.Title)}|{B(m.Artist)}|{B(m.Cover)}|{m.DurationSeconds}|{B(m.Duration)}\n");
        foreach (var n in d.Nodes.OrderBy(n => n.Id.ToString("D"), StringComparer.Ordinal))
            s.Append($"n|{G(n.Id)}|{B(n.Name)}|{G(n.ParentId)}|{n.Rank}\n");
        foreach (var e in d.Entries.OrderBy(e => e.Id.ToString("D"), StringComparer.Ordinal))
            s.Append($"e|{G(e.Id)}|{G(e.PlaylistId)}|{G(e.MediaId)}|{e.Rank}\n");
        foreach (var l in d.Likes.OrderBy(l => l.MediaId.ToString("D"), StringComparer.Ordinal))
            s.Append(CultureInfo.InvariantCulture, $"l|{G(l.MediaId)}|{l.AddedAt.ToUnixTimeMilliseconds()}|{l.Sequence}\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s.ToString()))).ToLowerInvariant();
    }
    public static LibraryDocument Apply(LibraryDocument d, LibraryPatch patch)
    {
        static List<T> Merge<T>(List<T> old, List<T> changed, List<Guid> removed, Func<T, Guid> key)
        { var map = old.ToDictionary(key); foreach (var id in removed) map.Remove(id); foreach (var item in changed) map[key(item)] = item; return map.Values.ToList(); }
        var next = d with
        {
            Media = Merge(d.Media, patch.Media, patch.RemovedMedia, m => m.Id),
            Nodes = Merge(d.Nodes, patch.Nodes, patch.RemovedNodes, n => n.Id),
            Entries = Merge(d.Entries, patch.Entries, patch.RemovedEntries, e => e.Id),
            Likes = Merge(d.Likes, patch.Likes, patch.RemovedLikes, l => l.MediaId)
        };
        LibraryGraph.Validate(next); return next;
    }
    public static LibraryPatch Diff(LibraryDocument a, LibraryDocument b)
    {
        static List<T> Changed<T>(List<T> a, List<T> b, Func<T, Guid> id) { var old = a.ToDictionary(id); return b.Where(v => !old.TryGetValue(id(v), out var before) || !EqualityComparer<T>.Default.Equals(v, before)).ToList(); }
        static List<Guid> Removed<T>(List<T> a, List<T> b, Func<T, Guid> id) => a.Select(id).Except(b.Select(id)).ToList();
        return new()
        {
            Media = Changed(a.Media, b.Media, m => m.Id),
            Nodes = Changed(a.Nodes, b.Nodes, n => n.Id),
            Entries = Changed(a.Entries, b.Entries, e => e.Id),
            Likes = Changed(a.Likes, b.Likes, l => l.MediaId),
            RemovedMedia = Removed(a.Media, b.Media, m => m.Id),
            RemovedNodes = Removed(a.Nodes, b.Nodes, n => n.Id),
            RemovedEntries = Removed(a.Entries, b.Entries, e => e.Id),
            RemovedLikes = Removed(a.Likes, b.Likes, l => l.MediaId)
        };
    }
}
