import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:hive_flutter/hive_flutter.dart';

import '../api/bilibili_client.dart';
import '../models/suggestion_model.dart';
import '../models/video_model.dart';

/// 搜索状态枚举
enum SearchState {
  /// 初始状态
  idle,

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
  static const int _debounceDelay = 300;

  // ==================== 构造 ====================

  SearchProvider({BilibiliClient? client})
    : _client = client ?? BilibiliClient() {
    debugPrint('SearchProvider initialized');
    _loadHistory();
  }

  @override
  void dispose() {
    _debounceTimer?.cancel();
    super.dispose();
  }

  // ==================== 公开方法 ====================

  /// 输入变化时调用（带防抖）
  ///
  /// 用户停止输入 300ms 后才会请求建议
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

    // 如果当前是在展示结果或错误状态，切换回初始状态以显示历史记录
    // 如果当前已有建议，保持显示旧建议，直到新建议加载完成
    if (_state == SearchState.showingResults ||
        _state == SearchState.searching ||
        _state == SearchState.error) {
      _state = SearchState.idle;
      notifyListeners();
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

    // 取消之前的建议请求计时器，避免搜索开始后触发建议请求
    _debounceTimer?.cancel();

    _currentKeyword = keyword;
    _currentPage = 1;
    _results = [];
    _suggestions = [];
    _state = SearchState.searching;
    _errorMessage = null;
    notifyListeners();

    // 添加到搜索历史
    await _addToHistory(keyword);

    try {
      final result = await _client.searchVideos(keyword);
      _results = result.videos;
      _hasMore = result.hasMore;
      // 成功获取结果后改变状态
      _state = SearchState.showingResults;
    } catch (e) {
      _errorMessage = e.toString();
      _state = SearchState.error;
    }

    notifyListeners();
  }

  /// 刷新当前搜索结果
  Future<void> refresh() async {
    if (_currentKeyword.trim().isEmpty) return;

    // 不改变状态为 searching，以免清空列表导致 RefreshIndicator 消失
    // 也不清空 _results，保留当前显示直到新数据回来
    _currentPage = 1;

    try {
      final result = await _client.searchVideos(_currentKeyword);
      _results = result.videos;
      _hasMore = result.hasMore;
      _state = SearchState.showingResults;
      _errorMessage = null;
    } catch (e) {
      debugPrint('Refresh failed: $e');
      // 刷新失败不改变状态，仅在控制台记录，或者可以抛出异常供 UI 处理
      rethrow;
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
  Future<void> clearHistory() async {
    _history = [];
    await _saveHistory();
    notifyListeners();
  }

  /// 从历史中删除某项
  Future<void> removeFromHistory(String keyword) async {
    _history.remove(keyword);
    await _saveHistory();
    notifyListeners();
  }

  // ==================== 私有方法 ====================

  void _loadHistory() {
    try {
      final box = Hive.box('settings');
      final history = box.get('search_history', defaultValue: <String>[]);
      if (history is List) {
        _history = history.cast<String>().toList();
      }
    } catch (e) {
      debugPrint('Error loading search history: $e');
    }
  }

  Future<void> _saveHistory() async {
    try {
      final box = Hive.box('settings');
      await box.put('search_history', _history);
    } catch (e) {
      debugPrint('Error saving search history: $e');
    }
  }

  Future<void> _fetchSuggestions(String keyword) async {
    if (keyword.isEmpty) return;
    try {
      final suggestions = await _client.fetchSuggestions(keyword);
      // 只有当关键词仍匹配时才更新（防止旧请求覆盖新请求）
      if (_currentKeyword == keyword) {
        _suggestions = suggestions;
        _state = SearchState.showingSuggestions;
      }
    } catch (e) {
      // ignore error
      debugPrint('Fetch suggestions error: $e');
    }
    notifyListeners();
  }

  Future<void> _addToHistory(String keyword) async {
    if (_history.contains(keyword)) {
      _history.remove(keyword);
    }
    _history.insert(0, keyword);
    if (_history.length > 10) {
      _history.removeLast();
    }
    await _saveHistory();
  }
}
