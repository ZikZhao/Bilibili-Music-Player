import 'package:flutter/foundation.dart';
import 'package:hive_flutter/hive_flutter.dart';

import '../models/video_model.dart';

/// 收藏库状态管理
///
/// 管理用户本地收藏的视频列表
class LibraryProvider extends ChangeNotifier {
  static const String _boxName = 'favorites';

  late Box<VideoModel> _favoritesBox;
  bool _isInitialized = false;

  /// 是否已初始化
  bool get isInitialized => _isInitialized;

  /// 获取所有收藏的视频
  List<VideoModel> get favorites {
    if (!_isInitialized) return [];
    return _favoritesBox.values.toList();
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
  Future<bool> toggleFavorite(VideoModel video) async {
    if (!_isInitialized) return false;

    if (isFavorite(video)) {
      await _favoritesBox.delete(video.bvid);
      notifyListeners();
      return false;
    } else {
      await _favoritesBox.put(video.bvid, video);
      notifyListeners();
      return true;
    }
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
