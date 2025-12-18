import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../providers/search_provider.dart';
import '../widgets/bili_app_bar.dart';
import '../widgets/video_result_card.dart';
import 'video_detail_page.dart';

class SearchPage extends StatefulWidget {
  const SearchPage({super.key});

  @override
  State<SearchPage> createState() => _SearchPageState();
}

class _SearchPageState extends State<SearchPage> {
  final TextEditingController _searchController = TextEditingController();
  final FocusNode _focusNode = FocusNode();
  final ScrollController _scrollController = ScrollController();

  @override
  void initState() {
    super.initState();
    _scrollController.addListener(_onScroll);
  }

  @override
  void dispose() {
    _searchController.dispose();
    _focusNode.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (_scrollController.position.pixels >=
        _scrollController.position.maxScrollExtent - 200) {
      context.read<SearchProvider>().loadMore();
    }
  }

  void _onInputChanged(String value) {
    context.read<SearchProvider>().onInputChanged(value);
    // 触发重建以更新清除按钮
    setState(() {});
  }

  void _onSearch(String keyword) {
    if (keyword.trim().isEmpty) return;
    _focusNode.unfocus();
    context.read<SearchProvider>().search(keyword);
  }

  void _onSuggestionTap(String keyword) {
    _searchController.text = keyword;
    _onSearch(keyword);
  }

  void _onClear() {
    _searchController.clear();
    context.read<SearchProvider>().clearSearch();
    setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: const BiliAppBar(title: '搜索音乐'),
      body: Column(
        children: [
          // 搜索框
          _buildSearchField(colorScheme),

          // 内容区域
          Expanded(
            child: Consumer<SearchProvider>(
              builder: (context, provider, child) {
                return _buildContent(provider, colorScheme);
              },
            ),
          ),
        ],
      ),
    );
  }

  /// 构建搜索框
  Widget _buildSearchField(ColorScheme colorScheme) {
    return Padding(
      padding: const EdgeInsets.all(16),
      child: TextField(
        controller: _searchController,
        focusNode: _focusNode,
        decoration: InputDecoration(
          filled: true,
          // 使用主题定义的颜色，无需在此处手动判断
          // fillColor: ... 
          hintText: '搜索 Bilibili 视频...',
          hintStyle: TextStyle(color: colorScheme.onSurfaceVariant),
          prefixIcon: Padding(
            padding: const EdgeInsets.only(left: 8, right: 4),
            child: Icon(Icons.search_rounded, color: colorScheme.primary),
          ),
          suffixIcon: _searchController.text.isNotEmpty
              ? IconButton(
                  icon: const Icon(Icons.clear_rounded, size: 20),
                  style: IconButton.styleFrom(
                    shape: const CircleBorder(), // 确保按下效果是圆形
                  ),
                  onPressed: _onClear,
                )
              : null,
        ),
        textInputAction: TextInputAction.search,
        onChanged: _onInputChanged,
        onSubmitted: _onSearch,
      ),
    );
  }

  /// 构建内容区域（根据状态切换）
  Widget _buildContent(SearchProvider provider, ColorScheme colorScheme) {
    switch (provider.state) {
      case SearchState.idle:
        return _buildIdleState(provider, colorScheme);

      case SearchState.showingSuggestions:
        return _buildSuggestionsList(provider, colorScheme);

      case SearchState.searching:
        return _buildSearchingState(colorScheme);

      case SearchState.showingResults:
        return _buildResultsList(provider, colorScheme);

      case SearchState.error:
        return _buildErrorState(provider, colorScheme);
    }
  }

  /// 初始状态：显示搜索历史或热门推荐
  Widget _buildIdleState(SearchProvider provider, ColorScheme colorScheme) {
    if (provider.history.isNotEmpty) {
      return _buildHistoryList(provider, colorScheme);
    }
    return _buildEmptyState(colorScheme);
  }

  /// 空状态
  Widget _buildEmptyState(ColorScheme colorScheme) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(
            Icons.search_rounded,
            size: 80,
            color: colorScheme.primary.withValues(alpha: 0.3),
          ),
          const SizedBox(height: 16),
          Text(
            '搜索你喜欢的音乐',
            style: TextStyle(fontSize: 16, color: Colors.grey.shade500),
          ),
        ],
      ),
    );
  }

  /// 搜索历史列表
  Widget _buildHistoryList(SearchProvider provider, ColorScheme colorScheme) {
    return ListView(
      padding: const EdgeInsets.symmetric(horizontal: 16),
      children: [
        // 标题行
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Row(
              children: [
                Icon(
                  Icons.history_rounded,
                  color: colorScheme.secondary,
                  size: 20,
                ),
                const SizedBox(width: 8),
                Text(
                  '搜索历史',
                  style: TextStyle(
                    color: colorScheme.secondary,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
            if (provider.history.isNotEmpty)
              TextButton(
                onPressed: provider.clearHistory,
                child: Text('清空', style: TextStyle(color: Colors.grey.shade500)),
              ),
          ],
        ),

        const SizedBox(height: 8),

        // 历史标签
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: provider.history.map((keyword) {
            return ActionChip(
              label: Text(keyword),
              onPressed: () => _onSuggestionTap(keyword),
              // 使用主题默认样式
              // backgroundColor: ...
              // side: ...
              // shape: ...
            );
          }).toList(),
        ),
      ],
    );
  }

  /// 建议列表
  Widget _buildSuggestionsList(
    SearchProvider provider,
    ColorScheme colorScheme,
  ) {
    return ListView.builder(
      padding: const EdgeInsets.symmetric(horizontal: 16),
      itemCount: provider.suggestions.length,
      itemBuilder: (context, index) {
        final suggestion = provider.suggestions[index];
        return ListTile(
          // Align with Search Bar icon: padding + size adjustment
          contentPadding: const EdgeInsets.symmetric(horizontal: 16),
          minLeadingWidth: 20,
          leading: Icon(
            Icons.search_rounded,
            color: Colors.grey.shade500,
            size: 20, // Match Search Bar icon size
          ),
          title: Text(suggestion.value),
          onTap: () => _onSuggestionTap(suggestion.value),
          // Remove meaningless trailing arrow
          trailing: null,
        );
      },
    );
  }

  /// 搜索中状态
  Widget _buildSearchingState(ColorScheme colorScheme) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            CircularProgressIndicator(
              color: colorScheme.primary,
              strokeWidth: 3,
            ),
            const SizedBox(height: 24),
            Text(
              '正在搜索...',
              style: TextStyle(
                fontSize: 16,
                color: Colors.grey.shade400,
                fontWeight: FontWeight.w500,
              ),
            ),
          ],
        ),
      ),
    );
  }

  /// 搜索结果列表
  Widget _buildResultsList(SearchProvider provider, ColorScheme colorScheme) {
    if (provider.results.isEmpty) {
      return _buildNoResultsState(colorScheme);
    }

    return RefreshIndicator(
      onRefresh: () async {
        await provider.refresh();
      },
      child: ListView.builder(
        controller: _scrollController,
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.symmetric(horizontal: 16),
        itemCount: provider.results.length + (provider.hasMore ? 1 : 0),
        itemBuilder: (context, index) {
          if (index == provider.results.length) {
            // 加载更多指示器
            return const Padding(
              padding: EdgeInsets.all(16),
              child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
            );
          }

          final video = provider.results[index];
          return VideoResultCard(
            video: video,
            onTap: () {
              Navigator.of(context).push(
                MaterialPageRoute(
                  builder: (context) => VideoDetailPage(video: video),
                ),
              );
            },
          );
        },
      ),
    );
  }

  /// 无结果状态
  Widget _buildNoResultsState(ColorScheme colorScheme) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(Icons.search_off_rounded, size: 80, color: Colors.grey.shade500),
          const SizedBox(height: 16),
          Text(
            '没有找到相关视频',
            style: TextStyle(fontSize: 16, color: Colors.grey.shade500),
          ),
        ],
      ),
    );
  }

  /// 错误状态
  Widget _buildErrorState(SearchProvider provider, ColorScheme colorScheme) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(
              Icons.error_outline_rounded,
              size: 64,
              color: colorScheme.error.withValues(alpha: 0.7),
            ),
            const SizedBox(height: 16),
            Text(
              '搜索出错了',
              style: TextStyle(fontSize: 16, color: Colors.grey.shade400),
            ),
            const SizedBox(height: 8),
            Text(
              provider.errorMessage ?? '未知错误',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 13, color: Colors.grey.shade500),
            ),
            const SizedBox(height: 24),
            FilledButton.icon(
              onPressed: () => _onSearch(_searchController.text),
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('重试'),
            ),
          ],
        ),
      ),
    );
  }
}
