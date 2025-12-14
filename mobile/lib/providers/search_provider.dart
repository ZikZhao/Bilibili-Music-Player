import 'dart:async';

import 'package:flutter/foundation.dart';

import '../api/bilibili_client.dart';
import '../models/suggestion_model.dart';
import '../models/video_model.dart';

/// 搜索状态枚举
enum SearchState {
  /// 初始状态
  idle,

  /// 加载建议中
  loadingSuggestions,

  /// 搜索中
  searching,

  /// 显示建议
  showingSuggestions,

  /// 显示结果
  showingResults,

  /// 错误
  error,
}

/// 搜索状态管理 Provider
///
/// 管理搜索相关的所有状态：
/// - 搜索建议（带防抖）
/// - 搜索结果
/// - 搜索历史
class SearchProvider extends ChangeNotifier {
  final BilibiliClient _client;

  // ==================== 状态 ====================

  SearchState _state = SearchState.idle;
  SearchState get state => _state;

  /// 搜索建议列表
  List<SuggestionModel> _suggestions = [];
  List<SuggestionModel> get suggestions => _suggestions;

  /// 搜索结果列表
  List<VideoModel> _results = [];
  List<VideoModel> get results => _results;

  /// 搜索历史（本地）
  List<String> _history = [];
  List<String> get history => _history;

  /// 当前搜索关键词
  String _currentKeyword = '';
  String get currentKeyword => _currentKeyword;

  /// 错误信息
  String? _errorMessage;
  String? get errorMessage => _errorMessage;

  /// 是否有更多结果
  bool _hasMore = false;
  bool get hasMore => _hasMore;

  /// 当前页码
  int _currentPage = 1;

  // ==================== 防抖 ====================

  /// 防抖计时器
  Timer? _debounceTimer;

  /// 防抖延迟（毫秒）
  static const int _debounceDelay = 500;

  // ==================== 构造 ====================

  SearchProvider({BilibiliClient? client})
      : _client = client ?? BilibiliClient();

  @override
  void dispose() {
    _debounceTimer?.cancel();
    super.dispose();
  }

  // ==================== 公开方法 ====================

  /// 输入变化时调用（带防抖）
  ///
  /// 用户停止输入 500ms 后才会请求建议
  void onInputChanged(String text) {
    _currentKeyword = text;

    // 取消之前的计时器
    _debounceTimer?.cancel();

    // 空输入时清空建议
    if (text.trim().isEmpty) {
      _suggestions = [];
      _state = SearchState.idle;
      notifyListeners();
      return;
    }

    // 设置新的防抖计时器
    _debounceTimer = Timer(
      const Duration(milliseconds: _debounceDelay),
      () => _fetchSuggestions(text),
    );
  }

  /// 执行搜索
  ///
  /// 当用户点击搜索按钮或从建议中选择时调用
  Future<void> search(String keyword) async {
    if (keyword.trim().isEmpty) return;

    _currentKeyword = keyword;
    _currentPage = 1;
    _results = [];
    _suggestions = [];
    _state = SearchState.searching;
    _errorMessage = null;
    notifyListeners();

    // 添加到搜索历史
    _addToHistory(keyword);

    try {
      final result = await _client.searchVideos(keyword);
      _results = result.videos;
      _hasMore = result.hasMore;
      _state = SearchState.showingResults;
    } catch (e) {
      _errorMessage = e.toString();
      _state = SearchState.error;
    }

    notifyListeners();
  }

  /// 加载更多结果
  Future<void> loadMore() async {
    if (!_hasMore || _state == SearchState.searching) return;

    _currentPage++;

    try {
      final result = await _client.searchVideos(
        _currentKeyword,
        page: _currentPage,
      );
      _results.addAll(result.videos);
      _hasMore = result.hasMore;
    } catch (e) {
      _currentPage--;
      // 加载更多失败不改变状态
    }

    notifyListeners();
  }

  /// 清空搜索
  void clearSearch() {
    _debounceTimer?.cancel();
    _currentKeyword = '';
    _suggestions = [];
    _results = [];
    _state = SearchState.idle;
    _errorMessage = null;
    notifyListeners();
  }

  /// 清空搜索历史
  void clearHistory() {
    _history = [];
    notifyListeners();
  }

  /// 从历史中删除某项
  void removeFromHistory(String keyword) {
    _history.remove(keyword);
    notifyListeners();
  }

  // ==================== 私有方法 ====================

  /// 获取搜索建议
  Future<void> _fetchSuggestions(String keyword) async {
    _state = SearchState.loadingSuggestions;
    notifyListeners();

    try {
      final suggestions = await _client.fetchSuggestions(keyword);
      _suggestions = suggestions;
      _state = suggestions.isEmpty
          ? SearchState.idle
          : SearchState.showingSuggestions;
    } catch (e) {
      _suggestions = [];
      _state = SearchState.idle;
    }

    notifyListeners();
  }

  /// 添加到搜索历史
  void _addToHistory(String keyword) {
    // 移除已存在的相同关键词
    _history.remove(keyword);
    // 添加到开头
    _history.insert(0, keyword);
    // 限制历史数量
    if (_history.length > 20) {
      _history = _history.sublist(0, 20);
    }
  }
}
