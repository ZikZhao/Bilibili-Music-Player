import 'video_model.dart';

/// 搜索结果
class SearchResult {
  final List<VideoModel> videos;
  final int page;
  final int pageSize;
  final int numResults;
  final int numPages;

  const SearchResult({
    required this.videos,
    required this.page,
    required this.pageSize,
    required this.numResults,
    required this.numPages,
  });

  bool get hasMore => page < numPages;
}
