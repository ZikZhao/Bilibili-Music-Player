using System.Collections.Generic;

namespace bilibili_music_player_windows.Models
{
    public sealed class SearchResult
    {
        public SearchResult(IReadOnlyList<VideoPreviewItem> videos, int page, int pageSize, int numResults, int numPages)
        {
            Videos = videos;
            Page = page;
            PageSize = pageSize;
            NumResults = numResults;
            NumPages = numPages;
        }

        public IReadOnlyList<VideoPreviewItem> Videos { get; }
        public int Page { get; }
        public int PageSize { get; }
        public int NumResults { get; }
        public int NumPages { get; }
    }
}
