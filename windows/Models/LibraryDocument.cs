using System;
using System.Collections.Generic;
using System.Text.Json;

namespace bilibili_music_player_windows.Models;

// A versioned, platform-neutral document. The cloud commits one complete graph
// with a revision check, so links, ordering and membership cannot arrive halfway.
public sealed record LibraryDocument
{
    public int Version { get; init; } = 1;
    public List<LibraryMedia> Media { get; init; } = new();
    public List<LibraryNode> Nodes { get; init; } = new();
    public List<LibraryEntry> Entries { get; init; } = new();
    public List<LibraryLike> Likes { get; init; } = new();
    public LibraryDocument Copy() => this with
    {
        Media = new(Media), Nodes = new(Nodes), Entries = new(Entries), Likes = new(Likes),
    };
}

public sealed record LibraryMedia(Guid Id, string Bvid, long Cid, string Title,
    string Artist, string Cover, long DurationSeconds, string Duration)
{
    public override string ToString() => Title;
}
public sealed record LibraryNode(Guid Id, string Kind, string Name, Guid? ParentId, long Order);
public sealed record LibraryEntry(Guid Id, Guid PlaylistId, Guid? MediaId, Guid? LinkedPlaylistId, long Order);
public sealed record LibraryLike(Guid MediaId, DateTimeOffset AddedAt);
public sealed record CloudLibrary(long Revision, LibraryDocument Document);
public sealed record ResolvedLibraryMedia(LibraryMedia Media, Guid? PlaylistId, Guid? EntryId);

public static class LibraryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
