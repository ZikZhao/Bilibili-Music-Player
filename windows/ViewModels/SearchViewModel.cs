#pragma warning disable MVVMTK0045

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Api;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace bilibili_music_player_windows.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly BilibiliClient _client;
    private readonly PlayerViewModel _player;
    private readonly LibraryViewModel _library;
    private VideoPreviewItem? _displayedPreview;
    private const string SearchHistoryKey = "search_history";

    public SearchSession Session { get; }
    public ObservableCollection<string> SearchHistory { get; } = new();
    public string SearchText { get => Session.SearchText; set => Session.SearchText = value; }
    public IReadOnlyList<VideoPreviewItem> SearchVideos => Session.Videos;
    public IReadOnlyList<SuggestionModel> Suggestions => Session.Suggestions;
    public bool HasSuggestions => Suggestions.Count > 0;
    public VideoPreviewItem? CurrentPreviewItem => Session.CurrentPreviewItem;
    public SearchStage Stage => Session.Stage;
    public bool IsPreviewing => Stage == SearchStage.Preview;
    public bool IsSearching => Session.IsSearching;
    public bool IsLoadingMore => Session.IsLoadingMore;
    public bool CanGoBack => Session.CanGoBack;
    public bool HasMore => Session.HasMore;
    public int TotalResults => Session.TotalResults;
    public string? ErrorMessage => Session.ErrorMessage;
    public string? LoadMoreError => Session.LoadMoreError;
    public string ResultSummary => $"“{Session.LastKeyword}” 的搜索结果 · {TotalResults} 个视频";

    public Visibility LandingVisibility => Visible(Stage == SearchStage.Landing);
    public Visibility ResultsVisibility => Visible(Stage == SearchStage.Results && !IsSearching && ErrorMessage is null);
    public Visibility PreviewModeVisibility => Visible(IsPreviewing);
    public Visibility SuggestionsVisibility => Visible(Suggestions.Count > 0);
    public Visibility IsSearchingVisibility => Visible(IsSearching);
    public Visibility ErrorMessageVisibility => Visible(Stage == SearchStage.Results && !IsSearching && ErrorMessage is not null);
    public Visibility EmptyResultsVisibility => Visible(Stage == SearchStage.Results && !IsSearching && ErrorMessage is null && SearchVideos.Count == 0);
    public Visibility HasMoreVisibility => Visible(HasMore && !IsSearching);
    public Visibility LoadingMoreVisibility => Visible(IsLoadingMore);
    public Visibility LoadMoreErrorVisibility => Visible(LoadMoreError is not null);
    public Visibility EmptyHistoryVisibility => Visible(SearchHistory.Count == 0);

    [ObservableProperty] private bool _isPreviewLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewErrorVisibility))]
    private string? _previewErrorMessage;
    public Visibility PreviewErrorVisibility => Visible(PreviewErrorMessage is not null);
    [ObservableProperty] private string _previewTitle = string.Empty;
    [ObservableProperty] private string _previewAuthor = string.Empty;
    [ObservableProperty] private string _previewViews = string.Empty;
    [ObservableProperty] private string _previewDuration = string.Empty;
    [ObservableProperty] private string _previewLikes = string.Empty;
    [ObservableProperty] private string _previewDescription = string.Empty;
    [ObservableProperty] private string _previewFavoriteCount = string.Empty;
    [ObservableProperty] private string _previewCoinCount = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFavoriteText))]
    private bool _isPreviewFavorited;
    public string PreviewFavoriteText => IsPreviewFavorited ? "已收藏" : "加入收藏";

    public SearchViewModel(BilibiliClient client, PlayerViewModel player, LibraryViewModel library)
    {
        _client = client;
        _player = player;
        _library = library;
        Session = new SearchSession(
            (query, page, ct) => _client.SearchVideosAsync(query, page, ct: ct),
            (query, ct) => _client.FetchSuggestionsAsync(query, ct));
        Session.StateChanged += OnSessionStateChanged;
        Session.SearchSubmitted += (_, query) => AddToHistory(query);
        LoadHistory();
    }

    private static Visibility Visible(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(_displayedPreview, CurrentPreviewItem))
        {
            _displayedPreview = CurrentPreviewItem;
            PreviewErrorMessage = null;
            IsPreviewLoading = IsPreviewing;
            if (CurrentPreviewItem is { } item)
            {
                PreviewTitle = item.Title;
                PreviewAuthor = item.Author;
                PreviewViews = item.Views;
                PreviewDuration = item.Duration;
                PreviewLikes = PreviewFavoriteCount = PreviewCoinCount = PreviewDescription = string.Empty;
                RefreshPreviewFavorite();
            }
        }

        foreach (var name in new[]
        {
            nameof(SearchText), nameof(SearchVideos), nameof(Suggestions), nameof(HasSuggestions), nameof(CurrentPreviewItem),
            nameof(Stage), nameof(IsPreviewing), nameof(IsSearching), nameof(IsLoadingMore), nameof(CanGoBack),
            nameof(HasMore), nameof(TotalResults), nameof(ResultSummary), nameof(ErrorMessage), nameof(LoadMoreError),
            nameof(LandingVisibility), nameof(ResultsVisibility), nameof(PreviewModeVisibility),
            nameof(SuggestionsVisibility), nameof(IsSearchingVisibility), nameof(ErrorMessageVisibility),
            nameof(EmptyResultsVisibility), nameof(HasMoreVisibility), nameof(LoadingMoreVisibility), nameof(LoadMoreErrorVisibility),
        }) OnPropertyChanged(name);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync(string? keyword) => Session.SubmitAsync(keyword);

    [RelayCommand]
    private Task LoadMoreAsync() => Session.LoadMoreAsync();

    [RelayCommand]
    private Task RefreshAsync() => Session.SubmitAsync(Session.LastKeyword, replaceHistory: true);

    public void EnterPreview(VideoPreviewItem item) => Session.OpenPreview(item);
    public void GoBack() => Session.GoBack();
    public void DismissSuggestions() => Session.DismissSuggestions();
    public void SetActive(bool active)
    {
        Session.SetSuggestionsEnabled(active);
        if (active) RefreshPreviewFavorite();
    }

    public void RefreshPreviewFavorite() =>
        IsPreviewFavorited = CurrentPreviewItem is { } item && _library.Favorites.Any(f => f.Bvid == item.Bvid);

    public async Task<Uri> LoadPreviewAsync(VideoPreviewItem item, CancellationToken ct)
    {
        await _player.PauseForPreviewAsync();
        ct.ThrowIfCancellationRequested();
        var detail = await _client.FetchVideoInfoAsync(item.Bvid, ct);
        ct.ThrowIfCancellationRequested();
        if (IsPreviewing && ReferenceEquals(CurrentPreviewItem, item))
        {
            PreviewTitle = detail.Title;
            PreviewAuthor = detail.Owner?.Name ?? item.Author;
            PreviewDescription = detail.Desc;
            PreviewDuration = detail.FormattedDuration;
            PreviewViews = FormatCount(detail.Stat.View);
            PreviewLikes = FormatCount(detail.Stat.Like);
            PreviewFavoriteCount = FormatCount(detail.Stat.Favorite);
            PreviewCoinCount = FormatCount(detail.Stat.Coin);
        }
        var playUrl = await _client.FetchPlayUrlAsync(item.Bvid, detail.Cid, audioOnly: false, ct);
        ct.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(playUrl.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new BilibiliApiException("获取到无效的播放地址。");
        return uri;
    }

    private static string FormatCount(long value) => value switch
    {
        >= 100_000_000 => $"{value / 100_000_000.0:F1}亿",
        >= 10_000 => $"{value / 10_000.0:F1}万",
        _ => value.ToString("N0"),
    };

    [RelayCommand]
    private async Task TogglePreviewFavoriteAsync()
    {
        if (CurrentPreviewItem is not { } item) return;
        await _library.ToggleFavoriteCommand.ExecuteAsync(VideoModel.FromPreviewItem(item));
        RefreshPreviewFavorite();
    }

    [RelayCommand]
    private void ClearHistory()
    {
        SearchHistory.Clear();
        SaveHistory();
        OnPropertyChanged(nameof(EmptyHistoryVisibility));
    }

    private void AddToHistory(string query)
    {
        SearchHistory.Remove(query);
        SearchHistory.Insert(0, query);
        while (SearchHistory.Count > 10) SearchHistory.RemoveAt(SearchHistory.Count - 1);
        SaveHistory();
        OnPropertyChanged(nameof(EmptyHistoryVisibility));
    }

    private void LoadHistory()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(SearchHistoryKey, out var value) &&
                value is string json)
                foreach (var query in JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>())
                    SearchHistory.Add(query);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Search] Load history: {ex.Message}"); }
    }

    private void SaveHistory()
    {
        try { ApplicationData.Current.LocalSettings.Values[SearchHistoryKey] = JsonSerializer.Serialize(SearchHistory.ToArray()); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Search] Save history: {ex.Message}"); }
    }
}
