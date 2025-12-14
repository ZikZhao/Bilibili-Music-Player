import 'package:flutter/foundation.dart';
import 'package:hive_flutter/hive_flutter.dart';

import '../api/bilibili_client.dart';
import '../models/video_model.dart';
import '../services/cache_manager.dart';

/// 排序选项枚举
enum SortOption {
  /// 最新添加优先
  dateNewest,

  /// 最早添加优先
  dateOldest,

  /// 按标题 A-Z
  titleAZ,
}

/// 收藏库状态管理
///
/// 管理用户本地收藏的视频列表
class LibraryProvider extends ChangeNotifier {
  static const String _boxName = 'favorites';

  late Box<VideoModel> _favoritesBox;
  bool _isInitialized = false;

  /// B 站 API 客户端（用于获取播放地址）
  final BilibiliClient _client = BilibiliClient();

  /// 当前排序方式
  SortOption _sortOption = SortOption.dateNewest;

  /// 获取当前排序方式
  SortOption get sortOption => _sortOption;

  /// 设置排序方式
  void setSortOption(SortOption option) {
    if (_sortOption != option) {
      _sortOption = option;
      notifyListeners();
    }
  }

  /// 是否已初始化
  bool get isInitialized => _isInitialized;

  /// 获取所有收藏的视频（根据当前排序方式排序）
  List<VideoModel> get favorites {
    if (!_isInitialized) return [];

    final list = _favoritesBox.values.toList();

    switch (_sortOption) {
      case SortOption.dateNewest:
        list.sort((a, b) => b.effectiveAddedAt.compareTo(a.effectiveAddedAt));
      case SortOption.dateOldest:
        list.sort((a, b) => a.effectiveAddedAt.compareTo(b.effectiveAddedAt));
      case SortOption.titleAZ:
        list.sort((a, b) => a.title.compareTo(b.title));
    }

    return list;
  }

  /// 收藏数量
  int get favoritesCount {
    if (!_isInitialized) return 0;
    return _favoritesBox.length;
  }

  /// 初始化 Hive Box
  Future<void> initialize() async {
    if (_isInitialized) return;

    _favoritesBox = await Hive.openBox<VideoModel>(_boxName);
    _isInitialized = true;
    notifyListeners();
  }

  /// 检查视频是否已收藏
  bool isFavorite(VideoModel video) {
    if (!_isInitialized) return false;
    return _favoritesBox.containsKey(video.bvid);
  }

  /// 切换收藏状态
  ///
  /// 返回操作后的收藏状态：true 表示已收藏，false 表示已取消收藏
  /// 收藏时自动触发后台下载缓存
  Future<bool> toggleFavorite(VideoModel video) async {
    if (!_isInitialized) return false;

    if (isFavorite(video)) {
      await _favoritesBox.delete(video.bvid);
      notifyListeners();
      return false;
    } else {
      // 添加收藏时记录时间
      final videoWithTime = video.copyWith(addedAt: DateTime.now());
      await _favoritesBox.put(video.bvid, videoWithTime);
      notifyListeners();

      // 触发后台缓存下载
      _triggerCacheDownload(video);

      return true;
    }
  }

  /// 触发后台缓存下载
  void _triggerCacheDownload(VideoModel video) {
    // 异步执行，不阻塞 UI
    Future(() async {
      try {
        // 检查是否已缓存
        if (await CacheManager.instance.isCached(video.bvid)) {
          debugPrint('[LibraryProvider] 已缓存，跳过下载: ${video.bvid}');
          return;
        }

        // 获取视频详情和播放地址
        final detail = await _client.fetchVideoInfo(video.bvid);

        // 更新收藏中的视频信息，使用正确的时长
        final updatedVideo = _favoritesBox.get(video.bvid);
        if (updatedVideo != null) {
          final videoWithCorrectDuration = updatedVideo.copyWith(
            duration: detail.formattedDuration,
          );
          await _favoritesBox.put(video.bvid, videoWithCorrectDuration);
          notifyListeners();
        }

        final playUrl = await _client.fetchPlayUrl(detail.bvid, detail.cid);

        // 后台下载
        CacheManager.instance.downloadInBackground(playUrl.url, video.bvid);
        debugPrint('[LibraryProvider] 已触发后台下载: ${video.bvid}');
      } catch (e) {
        debugPrint('[LibraryProvider] 触发后台下载失败: $e');
      }
    });
  }

  /// 添加到收藏
  Future<void> addFavorite(VideoModel video) async {
    if (!_isInitialized) return;
    if (!isFavorite(video)) {
      await _favoritesBox.put(video.bvid, video);
      notifyListeners();
    }
  }

  /// 从收藏移除
  Future<void> removeFavorite(VideoModel video) async {
    if (!_isInitialized) return;
    if (isFavorite(video)) {
      await _favoritesBox.delete(video.bvid);
      notifyListeners();
    }
  }

  /// 清空所有收藏
  Future<void> clearAll() async {
    if (!_isInitialized) return;
    await _favoritesBox.clear();
    notifyListeners();
  }
}
