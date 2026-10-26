using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Services;

public enum SearchStage { Landing, Results, Preview }

/// <summary>
/// UI-thread search session. Suggestions never change the browsing stage; every async
/// response must still own its request before it can publish data or clear a spinner.
/// </summary>
public sealed class SearchSession
{
    private readonly Func<string, int, CancellationToken, Task<SearchResult>> _search;
    private readonly Func<string, CancellationToken, Task<List<SuggestionModel>>> _suggest;
    private readonly TimeSpan _suggestionDelay;
    private readonly Stack<Snapshot> _history = new();
    private CancellationTokenSource? _searchRequest;
    private CancellationTokenSource? _suggestionRequest;
    private bool _suggestionsEnabled = true;
    private int _resultVersion;
    private string _searchText = string.Empty;

    public SearchSession(
        Func<string, int, CancellationToken, Task<SearchResult>> search,
        Func<string, CancellationToken, Task<List<SuggestionModel>>> suggest,
        TimeSpan? suggestionDelay = null)
    {
        _search = search;
        _suggest = suggest;
        _suggestionDelay = suggestionDelay ?? TimeSpan.FromMilliseconds(300);
    }

    public event EventHandler? StateChanged;
    public event EventHandler<string>? SearchSubmitted;
    public SearchStage Stage { get; private set; }
    public IReadOnlyList<VideoPreviewItem> Videos { get; private set; } = Array.Empty<VideoPreviewItem>();
    public IReadOnlyList<SuggestionModel> Suggestions { get; private set; } = Array.Empty<SuggestionModel>();
    public VideoPreviewItem? CurrentPreviewItem { get; private set; }
    public string LastKeyword { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }
    public string? LoadMoreError { get; private set; }
    public bool IsSearching { get; private set; }
    public bool IsLoadingMore { get; private set; }
    public int CurrentPage { get; private set; }
    public int TotalResults { get; private set; }
    public int NumPages { get; private set; }
    public bool HasMore => CurrentPage < NumPages;
    public bool CanGoBack => _history.Count > 0;
    public int ResultVersion => _resultVersion;
    public double ResultsScrollOffset { get; set; }
    public TimeSpan PreviewPosition { get; set; }
    public double PreviewVolume { get; set; } = 0.8;
    public bool PreviewMuted { get; set; }
    public double PreviewPlaybackRate { get; set; } = 1;

    public string SearchText
    {
        get => _searchText;
        set => _ = UpdateSearchTextAsync(value);
    }

    public Task UpdateSearchTextAsync(string value)
    {
        if (_searchText == value) return Task.CompletedTask;
        _searchText = value;
        DismissSuggestions();
        return _suggestionsEnabled && !string.IsNullOrWhiteSpace(value)
            ? RequestSuggestionsAsync(value) : Task.CompletedTask;
    }

    private async Task RequestSuggestionsAsync(string keyword)
    {
        using var request = new CancellationTokenSource();
        _suggestionRequest = request;
        var ct = request.Token;
        try
        {
            await Task.Delay(_suggestionDelay, ct);
            var suggestions = await _suggest(keyword, ct);
            if (ct.IsCancellationRequested || !ReferenceEquals(_suggestionRequest, request)) return;
            Suggestions = suggestions;
            Notify();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { /* Suggestions are optional and must not replace search results. */ }
        finally
        {
            if (ReferenceEquals(_suggestionRequest, request)) _suggestionRequest = null;
        }
    }

    public void DismissSuggestions()
    {
        var request = _suggestionRequest;
        _suggestionRequest = null;
        request?.Cancel();
        Suggestions = Array.Empty<SuggestionModel>();
        Notify();
    }

    public void SetSuggestionsEnabled(bool enabled)
    {
        _suggestionsEnabled = enabled;
        if (!enabled) DismissSuggestions();
    }

    public async Task SubmitAsync(string? keyword = null, bool replaceHistory = false)
    {
        var query = (keyword ?? SearchText).Trim();
        if (query.Length == 0) { DismissSuggestions(); return; }

        // Commit the transition before the first await, including cancellation of suggestions.
        DismissSuggestions();
        if (!replaceHistory && !IsSearching) _history.Push(Capture(submitting: true));
        CancelSearch();
        using var request = new CancellationTokenSource();
        _searchRequest = request;
        var ct = request.Token;
        Stage = SearchStage.Results;
        CurrentPreviewItem = null;
        _searchText = LastKeyword = query;
        IsSearching = true;
        IsLoadingMore = false;
        ErrorMessage = LoadMoreError = null;
        Videos = Array.Empty<VideoPreviewItem>();
        CurrentPage = TotalResults = NumPages = 0;
        ResultsScrollOffset = 0;
        _resultVersion++;
        Notify();
        SearchSubmitted?.Invoke(this, query);

        try
        {
            var result = await _search(query, 1, ct);
            if (!OwnsSearch(request)) return;
            Videos = result.Videos.ToArray();
            CurrentPage = result.Page;
            TotalResults = result.NumResults;
            NumPages = result.NumPages;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (OwnsSearch(request)) ErrorMessage = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_searchRequest, request))
            {
                _searchRequest = null;
                IsSearching = false;
                Notify();
            }
        }
    }

    public async Task LoadMoreAsync()
    {
        if (IsSearching || IsLoadingMore || !HasMore) return;
        using var request = new CancellationTokenSource();
        _searchRequest = request;
        var ct = request.Token;
        IsLoadingMore = true;
        LoadMoreError = null;
        Notify();
        try
        {
            var result = await _search(LastKeyword, CurrentPage + 1, ct);
            if (!OwnsSearch(request)) return;
            Videos = Videos.Concat(result.Videos).DistinctBy(video => video.Bvid).ToArray();
            CurrentPage = result.Page;
            NumPages = result.NumPages;
            TotalResults = result.NumResults;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (OwnsSearch(request)) LoadMoreError = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_searchRequest, request))
            {
                _searchRequest = null;
                IsLoadingMore = false;
                Notify();
            }
        }
    }

    public void OpenPreview(VideoPreviewItem item)
    {
        if (Stage != SearchStage.Preview || CurrentPreviewItem?.Bvid != item.Bvid) PreviewPosition = TimeSpan.Zero;
        if (Stage != SearchStage.Preview) _history.Push(Capture());
        DismissSuggestions();
        Stage = SearchStage.Preview;
        CurrentPreviewItem = item;
        Notify();
    }

    public void GoBack()
    {
        if (!_history.TryPop(out var previous)) return;
        DismissSuggestions();
        CancelSearch();
        Stage = previous.Stage;
        _searchText = previous.SearchText;
        LastKeyword = previous.Keyword;
        Videos = previous.Videos;
        CurrentPreviewItem = previous.Preview;
        CurrentPage = previous.Page;
        NumPages = previous.NumPages;
        TotalResults = previous.TotalResults;
        ResultsScrollOffset = previous.ScrollOffset;
        PreviewPosition = previous.PreviewPosition;
        _resultVersion = previous.ResultVersion;
        ErrorMessage = previous.Error;
        LoadMoreError = null;
        Notify();
    }

    private Snapshot Capture(bool submitting = false) => new(Stage,
        submitting ? (Stage == SearchStage.Landing ? string.Empty : LastKeyword) : SearchText,
        LastKeyword, Videos.ToArray(), CurrentPreviewItem, CurrentPage, NumPages,
        TotalResults, ResultsScrollOffset, _resultVersion, ErrorMessage, PreviewPosition);

    private bool OwnsSearch(CancellationTokenSource request) =>
        !request.IsCancellationRequested && ReferenceEquals(_searchRequest, request);

    private void CancelSearch()
    {
        var request = _searchRequest;
        _searchRequest = null;
        request?.Cancel();
        IsSearching = IsLoadingMore = false;
    }

    private void Notify() => StateChanged?.Invoke(this, EventArgs.Empty);

    private sealed record Snapshot(SearchStage Stage, string SearchText, string Keyword,
        IReadOnlyList<VideoPreviewItem> Videos, VideoPreviewItem? Preview, int Page,
        int NumPages, int TotalResults, double ScrollOffset, int ResultVersion, string? Error, TimeSpan PreviewPosition);
}
