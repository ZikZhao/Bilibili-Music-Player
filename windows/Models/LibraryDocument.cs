using System;
using System.Collections.Generic;
using System.Text.Json;

namespace bilibili_music_player_windows.Models;

// In-memory/wire projection of relational rows. Persistence uses separate tables;
// a delta and its outbox acknowledgement are committed together locally.
public sealed record LibraryDocument
{
    public int Version { get; init; } = 2;
    public List<LibraryMedia> Media { get; init; } = new();
    public List<LibraryNode> Nodes { get; init; } = new();
    public List<LibraryEntry> Entries { get; init; } = new();
    public List<LibraryLike> Likes { get; init; } = new();
    public LibraryDocument Copy() => this with
    {
        Media = new(Media),
        Nodes = new(Nodes),
        Entries = new(Entries),
        Likes = new(Likes),
    };
}

public sealed record LibraryMedia(Guid Id, string Bvid, long Cid, string Title,
    string Artist, string Cover, long DurationSeconds, string Duration)
{
    public override string ToString() => Title;
}
public sealed record LibraryNode(Guid Id, string Kind, string Name, Guid? ParentId, string Rank);
public sealed record LibraryEntry(Guid Id, Guid PlaylistId, Guid? MediaId, string Rank);
public sealed record LibraryLike(Guid MediaId, DateTimeOffset AddedAt, long Sequence = 0);
public sealed record ResolvedLibraryMedia(LibraryMedia Media, Guid? PlaylistId, Guid? EntryId);

public static class LibraryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed record LibraryTrackRef(string Bvid, long Cid, Guid EntryId);
public sealed record LibraryCommand
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public long Sequence { get; init; }
    public string Type { get; init; } = "";
    public Guid? NodeId { get; init; }
    public Guid? ParentId { get; init; }
    public Guid? ItemId { get; init; }
    public Guid? AnchorId { get; init; }
    public bool Before { get; init; }
    public string? Name { get; init; }
    public LibraryMedia? Media { get; init; }
    public string? Bvid { get; init; }
    public long Cid { get; init; }
    public bool Liked { get; init; }
    public long OccurredAt { get; init; }
    public List<LibraryTrackRef> Tracks { get; init; } = new();
}
public sealed record LibraryPatch
{
    public List<LibraryMedia> Media { get; init; } = new();
    public List<LibraryNode> Nodes { get; init; } = new();
    public List<LibraryEntry> Entries { get; init; } = new();
    public List<LibraryLike> Likes { get; init; } = new();
    public List<Guid> RemovedMedia { get; init; } = new();
    public List<Guid> RemovedNodes { get; init; } = new();
    public List<Guid> RemovedEntries { get; init; } = new();
    public List<Guid> RemovedLikes { get; init; } = new();
}
public sealed record LibraryChange(long Revision, string PreviousHash, string Hash, LibraryPatch Patch);
public sealed record LibrarySyncReply(string Mode, long Revision, string Hash, LibraryDocument? Document,
    List<LibraryChange> Changes, long Acknowledged = 0);
