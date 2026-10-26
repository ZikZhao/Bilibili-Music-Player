using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Services;

internal static class SearchSessionTests
{
    private static readonly VideoPreviewItem A = new() { Bvid = "BV-a", Title = "A" };
    private static readonly VideoPreviewItem B = new() { Bvid = "BV-b", Title = "B" };
    private static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static SearchResult Result(VideoPreviewItem item, int page = 1, int pages = 1) => new([item], page, 20, pages * 20, pages);
    private static List<SuggestionModel> Suggestion(string value) => [new() { Name = value, Value = value }];
    private static SearchSession Session(
        Func<string, int, CancellationToken, Task<SearchResult>>? search = null,
        Func<string, CancellationToken, Task<List<SuggestionModel>>>? suggest = null) =>
        new(search ?? ((_, _, _) => Task.FromResult(Result(A))),
            suggest ?? ((query, _) => Task.FromResult(Suggestion(query))), TimeSpan.Zero);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("typing requests suggestions without submitting a search", async () =>
        {
            var requests = 0;
            var session = Session((_, _, _) => { requests++; return Task.FromResult(Result(A)); });
            await session.UpdateSearchTextAsync("music");
            Check(requests == 0 && session.Stage == SearchStage.Landing && !session.CanGoBack, "Typing navigated or searched.");
            Check(session.Suggestions.Single().Value == "music", "Suggestions were not fetched.");
        }),
        ("submit immediately cancels in-flight suggestions and hides them", async () =>
        {
            var suggestions = Gate<List<SuggestionModel>>();
            var search = Gate<SearchResult>();
            CancellationToken suggestionToken = default;
            var session = Session((_, _, _) => search.Task, (_, ct) => { suggestionToken = ct; return suggestions.Task; });
            var typing = session.UpdateSearchTextAsync("music");
            var submitting = session.SubmitAsync();
            Check(suggestionToken.IsCancellationRequested, "Submit did not cancel the suggestion HTTP token.");
            Check(session.Stage == SearchStage.Results && session.IsSearching && session.Suggestions.Count == 0,
                "Loading transition did not occur synchronously.");
            suggestions.SetResult(Suggestion("stale"));
            await typing;
            Check(session.Suggestions.Count == 0 && session.IsSearching, "Late suggestions changed the loading screen.");
            search.SetResult(Result(A));
            await submitting;
            Check(session.Videos.Single() == A && !session.IsSearching, "Search did not finish.");
        }),
        ("newer candidate responses win over older ones", async () =>
        {
            var old = Gate<List<SuggestionModel>>();
            var session = Session(suggest: (query, _) => query == "old" ? old.Task : Task.FromResult(Suggestion(query)));
            var oldTyping = session.UpdateSearchTextAsync("old");
            await session.UpdateSearchTextAsync("new");
            old.SetResult(Suggestion("old"));
            await oldTyping;
            Check(session.Suggestions.Single().Value == "new", "An old candidate response overwrote new input.");
        }),
        ("editing a query leaves the existing results and back history intact", async () =>
        {
            var session = Session();
            await session.SubmitAsync("original");
            await session.UpdateSearchTextAsync("draft");
            Check(session.Stage == SearchStage.Results && session.Videos.Single() == A && session.LastKeyword == "original",
                "Suggestions replaced the result stage or committed query.");
            await session.UpdateSearchTextAsync("");
            Check(session.Stage == SearchStage.Results && session.Videos.Count == 1, "Clearing the input erased results.");
            session.GoBack();
            Check(session.Stage == SearchStage.Landing && !session.CanGoBack, "Results did not return to the initial screen.");
        }),
        ("new search cancels the old query and owns its spinner", async () =>
        {
            var old = Gate<SearchResult>();
            var newer = Gate<SearchResult>();
            CancellationToken oldToken = default;
            var session = Session((query, _, ct) => { if (query == "old") { oldToken = ct; return old.Task; } return newer.Task; });
            var first = session.SubmitAsync("old");
            var second = session.SubmitAsync("new");
            Check(oldToken.IsCancellationRequested, "New search did not cancel the previous query.");
            old.SetResult(Result(A));
            await first;
            Check(session.IsSearching && session.Videos.Count == 0, "The old completion cleared the new spinner.");
            newer.SetResult(Result(B));
            await second;
            Check(session.Videos.Single() == B && session.LastKeyword == "new", "The newest result was not retained.");
            session.GoBack();
            Check(session.Stage == SearchStage.Landing, "An unfinished superseded query leaked into history.");
        }),
        ("back during loading cancels the request and blocks its late result", async () =>
        {
            var response = Gate<SearchResult>();
            CancellationToken token = default;
            var session = Session((_, _, ct) => { token = ct; return response.Task; });
            var pending = session.SubmitAsync("query");
            session.GoBack();
            Check(token.IsCancellationRequested && session.Stage == SearchStage.Landing && !session.IsSearching,
                "Back did not cancel the in-flight search.");
            response.SetResult(Result(A));
            await pending;
            Check(session.Stage == SearchStage.Landing && session.Videos.Count == 0, "A cancelled search resurrected results.");
        }),
        ("switching preview videos creates one history entry and restores grid position", async () =>
        {
            var session = Session((_, _, _) => Task.FromResult(new SearchResult([A, B], 1, 20, 2, 1)));
            await session.SubmitAsync("query");
            session.ResultsScrollOffset = 582;
            await session.UpdateSearchTextAsync("unsubmitted draft");
            session.OpenPreview(A);
            session.OpenPreview(B);
            Check(session.Stage == SearchStage.Preview && session.CurrentPreviewItem == B, "Preview did not switch.");
            session.GoBack();
            Check(session.Stage == SearchStage.Results && session.Videos.Count == 2 && session.ResultsScrollOffset == 582 &&
                session.SearchText == "unsubmitted draft", "Back did not restore the exact result screen.");
            session.GoBack();
            Check(session.Stage == SearchStage.Landing && !session.CanGoBack, "Changing previews added extra history entries.");
        }),
        ("leaving and returning preserves preview and its history", async () =>
        {
            var session = Session();
            await session.SubmitAsync("query");
            session.OpenPreview(A);
            session.PreviewPosition = TimeSpan.FromSeconds(75);
            session.PreviewVolume = 0.35;
            session.PreviewMuted = true;
            session.PreviewPlaybackRate = 1.5;
            session.SetSuggestionsEnabled(false);
            session.SetSuggestionsEnabled(true);
            Check(session.Stage == SearchStage.Preview && session.CurrentPreviewItem == A && session.CanGoBack,
                "Page suspension erased the preview.");
            Check(session.PreviewPosition == TimeSpan.FromSeconds(75) && session.PreviewVolume == 0.35 &&
                session.PreviewMuted && session.PreviewPlaybackRate == 1.5, "Suspension erased playback settings or position.");
            session.GoBack();
            Check(session.Stage == SearchStage.Results, "The restored preview lost its back entry.");
        }),
        ("new searches are reversible and recover the previous query", async () =>
        {
            var session = Session((query, _, _) => Task.FromResult(Result(query == "first" ? A : B)));
            await session.SubmitAsync("first");
            session.ResultsScrollOffset = 300;
            await session.UpdateSearchTextAsync("second");
            await session.SubmitAsync();
            session.GoBack();
            Check(session.LastKeyword == "first" && session.SearchText == "first" && session.Videos.Single() == A &&
                session.ResultsScrollOffset == 300, "The previous search was not restored.");
        }),
        ("late pagination cannot mix old videos into a new query", async () =>
        {
            var page = Gate<SearchResult>();
            var session = Session((query, number, _) => number > 1 ? page.Task : Task.FromResult(Result(query == "old" ? A : B, 1, 2)));
            await session.SubmitAsync("old");
            var paging = session.LoadMoreAsync();
            await session.SubmitAsync("new");
            page.SetResult(Result(A, 2, 2));
            await paging;
            Check(session.LastKeyword == "new" && session.Videos.Single() == B && session.CurrentPage == 1 && !session.IsLoadingMore,
                "An old page polluted the new query.");
        }),
        ("search failures and empty results retain a navigable result stage", async () =>
        {
            var session = Session((_, _, _) => Task.FromException<SearchResult>(new IOException("offline")));
            await session.SubmitAsync("query");
            Check(session.Stage == SearchStage.Results && !session.IsSearching && session.ErrorMessage == "offline", "Error state is inconsistent.");
            session.GoBack();
            Check(session.Stage == SearchStage.Landing && session.ErrorMessage is null, "Back retained a search error.");
            var empty = Session((_, _, _) => Task.FromResult(new SearchResult([], 1, 20, 0, 0)));
            await empty.SubmitAsync("nothing");
            Check(empty.Stage == SearchStage.Results && !empty.HasMore && !empty.IsSearching && empty.CanGoBack, "Empty results lost the result stage.");
        }),
        ("leaving the page cancels candidates and late callbacks remain hidden", async () =>
        {
            var response = Gate<List<SuggestionModel>>();
            CancellationToken token = default;
            var session = Session(suggest: (_, ct) => { token = ct; return response.Task; });
            var typing = session.UpdateSearchTextAsync("draft");
            session.SetSuggestionsEnabled(false);
            Check(token.IsCancellationRequested, "Navigation did not cancel suggestion requests.");
            response.SetResult(Suggestion("late"));
            await typing;
            session.SetSuggestionsEnabled(true);
            Check(session.Suggestions.Count == 0 && session.SearchText == "draft" && session.Stage == SearchStage.Landing,
                "Late candidates reappeared after page navigation.");
        }),
        ("retry replaces the failed entry instead of extending history", async () =>
        {
            var attempt = 0;
            var session = Session((_, _, _) => ++attempt == 1
                ? Task.FromException<SearchResult>(new IOException("offline")) : Task.FromResult(Result(A)));
            await session.SubmitAsync("query");
            await session.SubmitAsync("query", replaceHistory: true);
            Check(session.ErrorMessage is null && session.Videos.Single() == A, "Retry retained the old error.");
            session.GoBack();
            Check(session.Stage == SearchStage.Landing && !session.CanGoBack, "Retry created a duplicate history entry.");
        }),
        ("searching from preview can restore the previous playback position", async () =>
        {
            var session = Session();
            await session.SubmitAsync("first");
            session.OpenPreview(A);
            session.PreviewPosition = TimeSpan.FromSeconds(90);
            await session.SubmitAsync("second");
            session.GoBack();
            Check(session.Stage == SearchStage.Preview && session.CurrentPreviewItem == A &&
                session.PreviewPosition == TimeSpan.FromSeconds(90), "Preview position was lost in history.");
        }),
        ("pagination keeps preview active and suppresses duplicate video cards", async () =>
        {
            var page = Gate<SearchResult>();
            var session = Session((_, number, _) => number == 1 ? Task.FromResult(Result(A, 1, 2)) : page.Task);
            await session.SubmitAsync("query");
            session.OpenPreview(A);
            var loading = session.LoadMoreAsync();
            Check(session.IsLoadingMore && !session.IsSearching && session.Stage == SearchStage.Preview,
                "Pagination replaced the active preview with a search spinner.");
            page.SetResult(new SearchResult([A, B], 2, 20, 2, 2));
            await loading;
            Check(session.Videos.Count == 2 && session.CurrentPreviewItem == A && !session.HasMore && !session.IsLoadingMore,
                "Pagination duplicated cards or changed the selected preview.");
        }),
    ];
}
