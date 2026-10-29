#pragma warning disable MVVMTK0045
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace bilibili_music_player_windows.ViewModels;

public sealed class LibraryNavItem : ObservableObject
{
    public string Name { get; }
    public string Glyph { get; }
    public Guid? Id { get; }
    public string Kind { get; }
    public ObservableCollection<LibraryNavItem> Children { get; }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    private bool _isExpanded = true;
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public LibraryNavItem(string name, string glyph, Guid? id, string kind, ObservableCollection<LibraryNavItem> children)
    { Name = name; Glyph = glyph; Id = id; Kind = kind; Children = children; }
}
public sealed record LibraryDisplayRow(string Title, string Subtitle, string Glyph, string Cover,
    string Duration, Guid? MediaId, Guid? NodeId, Guid? EntryId, string Kind, Guid? SourceId = null)
{
    public override string ToString() => Title;
}
public sealed record LibraryChoice(Guid? Id, string Name)
{
    public override string ToString() => Name;
}

public partial class LibraryViewModel : ObservableObject
{
    private readonly PlayerViewModel _player;
    private readonly SettingsViewModel _settings;
    public LibraryRepository Repository { get; }
    public SupabaseAccountService Account { get; }
    public Task Initialization { get; }
    private readonly Stack<(Guid? Id, string Kind)> _history = new();
    private LibraryDocument? _renderedDocument;
    public Guid? CurrentId { get; private set; }
    public string CurrentKind { get; private set; } = "likes";
    public bool CanGoBack => _history.Count > 0 || CurrentId.HasValue;
    public bool IsPlaylist => CurrentKind == "playlist";
    public bool IsNode => CurrentId.HasValue;
    public bool CanSortTracks => CurrentKind is not ("folder" or "likes") && (!IsPlaylist || ExpandLinks);
    public bool IsSignedIn => Account.Session is not null;
    public bool HasConflict => Repository.HasConflict;
    public string AccountLabel => Account.Session?.Email ?? "未登录";
    public string AccountButtonLabel => IsSignedIn ? "账户" : "邮箱登录";
    public string SyncStatus => Repository.Status;
    public LibraryDocument Document => Repository.State.Document;
    public ObservableCollection<LibraryNavItem> Roots { get; } = new();
    public ObservableCollection<LibraryNavItem> NavigationChoices { get; } = new();
    public ObservableCollection<LibraryDisplayRow> Rows { get; } = new();
    public ObservableCollection<VideoModel> Favorites { get; } = new();
    public ObservableCollection<string> SortOptions { get; } = new() { "自定义顺序", "标题" };
    [ObservableProperty] private string _sortOption = "自定义顺序";
    [ObservableProperty] private bool _expandLinks = true;
    [ObservableProperty] private string _heading = "我喜欢";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isReady;
    public Visibility EmptyStateVisibility => Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ListVisibility => Rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlaylistToolsVisibility => IsPlaylist ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NodeToolsVisibility => IsNode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ConflictVisibility => HasConflict ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SignedInVisibility => IsSignedIn ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TrackSortVisibility => CanSortTracks ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => string.IsNullOrEmpty(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
    public event EventHandler? LibraryChanged;
#if DEBUG
    private readonly Guid _inspectionProfile = Guid.NewGuid();
    public async Task InspectFixtureAsync(string mode)
    {
        await Initialization;
        _history.Clear(); CurrentId = null; CurrentKind = "likes";
        if (mode == "restore") { _player.ClearPlaylist(); await Repository.SwitchAsync(Account.Session?.UserId); return; }
        if (mode is not ("empty" or "sample")) throw new ArgumentException("Unknown library fixture.");
        await Repository.SwitchAsync(_inspectionProfile, synchronize: false);
        if (mode == "sample" && Document.Nodes.Count == 0)
            await Repository.EditAsync(g =>
            {
                var f = g.Create("folder", "界面验证 · 游戏原声"); var sub = g.Create("folder", "原神", f);
                var source = g.Create("playlist", "战斗音乐", sub); var linked = g.Create("playlist", "常听合集");
                var a = g.Remember(new(Guid.NewGuid(), "BV_fixture_a", 1, "示例曲目 A", "界面验证", "", 180, "3:00"));
                var b = g.Remember(new(Guid.NewGuid(), "BV_fixture_b", 1, "示例曲目 B", "界面验证", "", 240, "4:00"));
                g.AddMedia(source, new[] { a, b }); g.Link(linked, source); g.Like(a, true);
            });
        Rebuild();
    }
#endif

    public LibraryViewModel(PlayerViewModel player, LibraryRepository repository, SupabaseAccountService account, SettingsViewModel settings)
    {
        _player = player; Repository = repository; Account = account; _settings = settings;
        Repository.Changed += (_, _) => Rebuild();
        Account.Changed += OnAccountChanged;
        Initialization = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await Account.Initialization;
            await Repository.SwitchAsync(null);
            if (!Repository.State.LegacyImported)
            {
                var legacy = Path.Combine(ApplicationData.Current.LocalFolder.Path, "favorites.json");
                if (File.Exists(legacy))
                {
                    var old = JsonSerializer.Deserialize<List<VideoModel>>(await File.ReadAllTextAsync(legacy)) ?? new();
                    await Repository.EditAsync(graph =>
                    {
                        foreach (var video in old)
                        { var id = graph.Remember(FromVideo(video)); graph.Like(id, true); }
                    }, markLegacyImported: true);
                }
            }
            if (Account.Session is { } session) await Repository.SwitchAsync(session.UserId);
            IsReady = true;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        Rebuild();
    }

    private async void OnAccountChanged(object? sender, EventArgs e)
    {
        if (Repository.State.UserId == Account.Session?.UserId) { Rebuild(); return; }
        _player.ClearPlaylist();
        IsReady = false; _history.Clear(); CurrentId = null; CurrentKind = "likes";
        try { await Repository.SwitchAsync(Account.Session?.UserId); IsReady = true; }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        Rebuild();
    }

    partial void OnSortOptionChanged(string value) => Rebuild();
    partial void OnExpandLinksChanged(bool value) => Rebuild();
    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(ErrorVisibility));

    public void Open(Guid? id, string kind, bool remember = true)
    {
        if (id == CurrentId && kind == CurrentKind) return;
        if (id.HasValue && !Document.Nodes.Any(n => n.Id == id)) return;
        if (remember) _history.Push((CurrentId, CurrentKind));
        CurrentId = id; CurrentKind = kind; Rebuild();
        if (IsSignedIn) _ = Repository.SyncAsync();
    }
    public void GoBack()
    {
        var found = false;
        while (_history.TryPop(out var previous))
            if ((previous.Id != CurrentId || previous.Kind != CurrentKind) &&
                (previous.Id is null || Document.Nodes.Any(n => n.Id == previous.Id)))
            { CurrentId = previous.Id; CurrentKind = previous.Kind; found = true; break; }
        if (!found && CurrentId is { } id)
        {
            CurrentId = Document.Nodes.FirstOrDefault(n => n.Id == id)?.ParentId;
            CurrentKind = CurrentId.HasValue ? "folder" : "likes";
        }
        Rebuild();
    }
    public string PathName(Guid id)
    {
        var node = Document.Nodes.Single(n => n.Id == id);
        return node.ParentId is { } p ? PathName(p) + " / " + node.Name : node.Name;
    }
    public List<LibraryChoice> Folders() => new[] { new LibraryChoice(null, "收藏库根目录") }
        .Concat(Document.Nodes.Where(n => n.Kind == "folder").Select(n => new LibraryChoice(n.Id, PathName(n.Id)))).ToList();
    public List<LibraryChoice> Playlists() => Document.Nodes.Where(n => n.Kind == "playlist")
        .Select(n => new LibraryChoice(n.Id, PathName(n.Id))).ToList();
    public Guid? CreationParent => CurrentKind == "folder" ? CurrentId : Document.Nodes.FirstOrDefault(n => n.Id == CurrentId)?.ParentId;

    public async Task RunAsync(Func<Task> action)
    {
        try { await Initialization; ErrorMessage = ""; await action(); }
        catch (Exception ex) { ErrorMessage = ex is System.Net.Http.HttpRequestException ? "暂时无法连接，请稍后重试。" : ex.Message; }
    }
    public async Task CreateAsync(string kind, string name)
    {
        Guid id = default;
        await Repository.EditAsync(graph => id = graph.Create(kind, name, CreationParent));
        Open(id, kind);
    }
    public Task RenameAsync(string name) => Repository.EditAsync(graph => graph.Rename(CurrentId!.Value, name));
    public Task MoveAsync(Guid? parent) => Repository.EditAsync(graph => graph.Move(CurrentId!.Value, parent));
    public async Task DeleteAsync()
    {
        var id = CurrentId!.Value;
        var parent = Document.Nodes.Single(n => n.Id == id).ParentId;
        await Repository.EditAsync(graph => graph.Delete(id));
        Open(parent, parent.HasValue ? "folder" : "likes", remember: false);
    }
    public Task LinkAsync(Guid target) => Repository.EditAsync(graph => graph.Link(CurrentId!.Value, target));
    public Task AddMediaAsync(IEnumerable<Guid> media) => Repository.EditAsync(graph => graph.AddMedia(CurrentId!.Value, media));
    public Task CopyPlaylistAsync(Guid source) => AddMediaAsync(new LibraryGraph(Document).Expand(source).Select(m => m.Id));
    public Task RemoveEntryAsync(Guid id) => Repository.EditAsync(graph => graph.RemoveEntry(id));
    public Task ReorderEntryAsync(Guid id, int delta) => Repository.EditAsync(graph => graph.ReorderEntry(id, delta));
    public Task ReorderNodeAsync(int delta) => Repository.EditAsync(graph => graph.ReorderNode(CurrentId!.Value, delta));

    [RelayCommand] private Task ToggleFavoriteAsync(VideoModel? video) => RunAsync(async () =>
    {
        if (video is null) return;
        await Repository.EditAsync(graph =>
        {
            var id = graph.Remember(FromVideo(video));
            graph.Like(id, !graph.Document.Likes.Any(l => l.MediaId == id));
        });
    });
    [RelayCommand] private Task RemoveFavoriteAsync(VideoModel? video) => RunAsync(async () =>
    {
        if (video is null) return;
        var media = Document.Media.FirstOrDefault(m => m.Bvid == video.Bvid && m.Cid == video.Cid);
        if (media is not null) await Repository.EditAsync(graph => graph.Like(media.Id, false));
    });
    [RelayCommand] private Task SortAsync(string? option) { if (SortOptions.Contains(option!)) SortOption = option!; return Task.CompletedTask; }
    [RelayCommand] private Task PlayFromFavoriteAsync(VideoModel? video) => RunAsync(async () =>
    {
        if (video is null) return;
        var list = new LibraryGraph(Document).Expand(null, likes: true).Select(ToVideo).ToList();
        var index = list.FindIndex(v => v.Bvid == video.Bvid && v.Cid == video.Cid);
        if (index >= 0) await _player.SetPlaylistAsync(list, index);
    });

    public List<LibraryMedia> CurrentTracks()
    {
        var list = new LibraryGraph(Document).Expand(CurrentId, CurrentKind == "likes").ToList();
        return CanSortTracks && SortOption == "标题" ? list.OrderBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase).ToList() : list;
    }
    public async Task PlayAsync(LibraryDisplayRow? start = null)
    {
        var tracks = CurrentTracks();
        var index = start?.MediaId is { } id ? Math.Max(0, tracks.FindIndex(m => m.Id == id)) : 0;
        if (tracks.Count > 0) await _player.SetPlaylistAsync(tracks.Select(ToVideo).ToList(), index);
    }
    public async Task QuickPlayAsync(LibraryDisplayRow row)
    {
        if (row.MediaId is not { } id) return;
        var tracks = _settings.QuickPlayScope == 1
            ? Document.Media.Where(m => m.Id == id).ToList() : CurrentTracks();
        var index = tracks.FindIndex(m => m.Id == id);
        if (index < 0) return;
        await _player.QuickPlayAsync(tracks.Select(ToVideo).ToList(), index,
            _settings.QuickPlayDestination, _settings.EnableAutoPlay);
    }
    public void Queue(bool next, LibraryDisplayRow? row = null)
    {
        var items = row?.MediaId is { } m ? Document.Media.Where(v => v.Id == m).ToList() :
            row?.NodeId is { } n ? new LibraryGraph(Document).Expand(n).ToList() : CurrentTracks();
        _player.Enqueue(items.Select(ToVideo).ToList(), next);
    }
    public Task UnlikeAsync(Guid id) => Repository.EditAsync(graph => graph.Like(id, false));

    private void Rebuild()
    {
        if (CurrentId.HasValue && !Document.Nodes.Any(n => n.Id == CurrentId)) { CurrentId = null; CurrentKind = "likes"; _history.Clear(); }
        var graph = new LibraryGraph(Document);
        var expansion = new Dictionary<(Guid?, string), bool>();
        void RememberExpansion(IEnumerable<LibraryNavItem> items)
        { foreach (var item in items) { expansion[(item.Id, item.Kind)] = item.IsExpanded; RememberExpansion(item.Children); } }
        RememberExpansion(Roots);
        ObservableCollection<LibraryNavItem> Children(Guid? parent) => new(Document.Nodes.Where(n => n.ParentId == parent)
            .OrderBy(n => n.Order).ThenBy(n => n.Id).Select(n => new LibraryNavItem(n.Name,
                n.Kind == "folder" ? "\uE8B7" : "\uE142", n.Id, n.Kind, Children(n.Id))
                { IsExpanded = expansion.GetValueOrDefault((n.Id, n.Kind), true) }));
        if (!ReferenceEquals(Document, _renderedDocument))
        {
            _renderedDocument = Document;
            Roots.Clear();
            Roots.Add(new("我喜欢", "\uEB51", null, "likes", new()));
            Roots.Add(new("全部收藏", "\uE8F1", null, "all", new()));
            foreach (var item in Children(null)) Roots.Add(item);
            NavigationChoices.Clear();
            NavigationChoices.Add(Roots[0]); NavigationChoices.Add(Roots[1]);
            foreach (var node in Document.Nodes.OrderBy(n => PathName(n.Id), StringComparer.CurrentCulture))
                NavigationChoices.Add(new(PathName(node.Id), node.Kind == "folder" ? "\uE8B7" : "\uE142", node.Id, node.Kind, new()));
            Favorites.Clear();
            foreach (var item in graph.Expand(null, likes: true)) Favorites.Add(ToVideo(item));
        }
        var nextRows = new List<LibraryDisplayRow>();
        Heading = CurrentKind switch { "likes" => "我喜欢", "all" => "全部收藏", _ => PathName(CurrentId!.Value) };
        if (CurrentKind == "folder")
            foreach (var node in Document.Nodes.Where(n => n.ParentId == CurrentId).OrderBy(n => n.Order))
                nextRows.Add(new(node.Name, node.Kind == "folder" ? "文件夹" : $"歌单 · {graph.Expand(node.Id).Count} 首",
                    node.Kind == "folder" ? "\uE8B7" : "\uE142", "", "", null, node.Id, null, node.Kind));
        else if (IsPlaylist && !ExpandLinks)
            foreach (var entry in Document.Entries.Where(e => e.PlaylistId == CurrentId).OrderBy(e => e.Order).ThenBy(e => e.Id))
            {
                if (entry.MediaId is { } id) nextRows.Add(MediaRow(Document.Media.Single(m => m.Id == id), entry.Id));
                else if (entry.LinkedPlaylistId is { } target)
                    nextRows.Add(new(Document.Nodes.Single(n => n.Id == target).Name, $"动态链接 · {graph.Expand(target).Count} 首 · 跟随源歌单更新",
                        "\uE71B", "", "", null, target, entry.Id, "link"));
            }
        else
        {
            var resolved = graph.Resolve(CurrentId, CurrentKind == "likes").ToDictionary(r => r.Media.Id);
            var collectedAt = CurrentKind == "likes" ? Document.Likes.ToDictionary(l => l.MediaId, l => l.AddedAt) : null;
            foreach (var media in CurrentTracks())
            {
                var origin = resolved[media.Id];
                var row = MediaRow(media, IsPlaylist && origin.PlaylistId == CurrentId ? origin.EntryId : null);
                if (collectedAt is not null)
                {
                    var date = collectedAt[media.Id].ToLocalTime().ToString("yyyy-MM-dd");
                    row = row with { Subtitle = "收藏于 " + date + (string.IsNullOrEmpty(media.Artist) ? "" : " · " + media.Artist) };
                }
                if (IsPlaylist && origin.PlaylistId is { } source && source != CurrentId)
                    row = row with { SourceId = source, Subtitle = media.Artist + " · 来自“" + Document.Nodes.Single(n => n.Id == source).Name + "”" };
                nextRows.Add(row);
            }
        }
        // Keep existing containers and scroll position on status-only updates.
        for (var i = 0; i < Math.Min(Rows.Count, nextRows.Count); i++)
            if (Rows[i] != nextRows[i]) Rows[i] = nextRows[i];
        while (Rows.Count > nextRows.Count) Rows.RemoveAt(Rows.Count - 1);
        for (var i = Rows.Count; i < nextRows.Count; i++) Rows.Add(nextRows[i]);
        Summary = CurrentKind == "likes" ? $"{CurrentTracks().Count} 首 · 收藏时间线 · 最近收藏在前" :
            CurrentKind == "folder" ? $"{Rows.Count} 个项目 · 播放时包含所有子文件夹" :
            $"{CurrentTracks().Count} 首" + (IsPlaylist ? " · 动态链接在打开和播放时展开" : "");
        foreach (var name in new[] { nameof(IsPlaylist), nameof(IsNode), nameof(CanGoBack), nameof(IsSignedIn), nameof(AccountLabel), nameof(AccountButtonLabel),
            nameof(SyncStatus), nameof(HasConflict), nameof(EmptyStateVisibility), nameof(ListVisibility), nameof(PlaylistToolsVisibility),
            nameof(NodeToolsVisibility), nameof(ConflictVisibility), nameof(SignedInVisibility), nameof(TrackSortVisibility) }) OnPropertyChanged(name);
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    private static LibraryDisplayRow MediaRow(LibraryMedia m, Guid? entry) => new(m.Title, m.Artist, "\uE8D6", m.Cover, m.Duration,
        m.Id, null, entry, "track");
    private static LibraryMedia FromVideo(VideoModel v) => new(Guid.NewGuid(), v.Bvid, v.Cid, v.Title, v.Artist, v.Cover, v.DurationSeconds, v.Duration);
    public static VideoModel ToVideo(LibraryMedia m) => new() { Bvid = m.Bvid, Cid = m.Cid, Title = m.Title,
        Artist = m.Artist, Cover = m.Cover, DurationSeconds = m.DurationSeconds, Duration = m.Duration };
}
