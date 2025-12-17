import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/video_model.dart';
import '../../providers/library_provider.dart';
import '../../providers/player_provider.dart';
import '../widgets/video_result_card.dart';

class LibraryPage extends StatelessWidget {
  const LibraryPage({super.key});

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Text(
          '我的收藏',
          style: TextStyle(
            color: colorScheme.primary,
            fontWeight: FontWeight.bold,
          ),
        ),
        actions: [
          Consumer<LibraryProvider>(
            builder: (context, provider, _) {
              return PopupMenuButton<SortOption>(
                icon: Icon(Icons.sort_rounded, color: colorScheme.secondary),
                tooltip: '排序方式',
                onSelected: (option) => provider.setSortOption(option),
                itemBuilder: (context) => [
                  _buildSortMenuItem(
                    SortOption.dateNewest,
                    '最新添加',
                    Icons.arrow_downward_rounded,
                    provider.sortOption,
                    colorScheme,
                  ),
                  _buildSortMenuItem(
                    SortOption.dateOldest,
                    '最早添加',
                    Icons.arrow_upward_rounded,
                    provider.sortOption,
                    colorScheme,
                  ),
                  _buildSortMenuItem(
                    SortOption.titleAZ,
                    '按标题 A-Z',
                    Icons.sort_by_alpha_rounded,
                    provider.sortOption,
                    colorScheme,
                  ),
                ],
              );
            },
          ),
        ],
      ),
      body: Consumer<LibraryProvider>(
        builder: (context, libraryProvider, child) {
          if (!libraryProvider.isInitialized) {
            return const Center(child: CircularProgressIndicator());
          }

          final favorites = libraryProvider.favorites;

          if (favorites.isEmpty) {
            return _buildEmptyState(colorScheme);
          }

          return _buildFavoritesList(context, favorites);
        },
      ),
    );
  }

  /// 构建排序菜单项
  PopupMenuItem<SortOption> _buildSortMenuItem(
    SortOption option,
    String label,
    IconData icon,
    SortOption currentOption,
    ColorScheme colorScheme,
  ) {
    final isSelected = option == currentOption;
    return PopupMenuItem<SortOption>(
      value: option,
      child: Row(
        children: [
          Icon(
            icon,
            size: 20,
            color: isSelected ? colorScheme.primary : Colors.grey,
          ),
          const SizedBox(width: 12),
          Text(
            label,
            style: TextStyle(
              color: isSelected ? colorScheme.primary : null,
              fontWeight: isSelected ? FontWeight.w600 : null,
            ),
          ),
          const Spacer(),
          if (isSelected)
            Icon(Icons.check_rounded, size: 18, color: colorScheme.primary),
        ],
      ),
    );
  }

  Widget _buildFavoritesList(BuildContext context, List<VideoModel> favorites) {
    return ListView.builder(
      padding: const EdgeInsets.only(top: 16, bottom: 16, left: 16, right: 16),
      itemCount: favorites.length,
      itemBuilder: (context, index) {
        final video = favorites[index];
        return VideoResultCard(
          video: video,
          onTap: () => _playFromLibrary(context, favorites, index),
        );
      },
    );
  }

  /// 从收藏列表播放
  ///
  /// 点击歌曲直接播放，Mini Player 会自动显示
  Future<void> _playFromLibrary(
    BuildContext context,
    List<VideoModel> favorites,
    int startIndex,
  ) async {
    final playerProvider = context.read<PlayerProvider>();

    // 设置播放列表并指定起始索引
    playerProvider.setPlaylist(favorites, startIndex: startIndex);

    // 开始播放
    await playerProvider.playVideo(favorites[startIndex]);
  }

  Widget _buildEmptyState(ColorScheme colorScheme) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          // 空状态图标
          Container(
            width: 120,
            height: 120,
            decoration: BoxDecoration(
              color: colorScheme.primary.withValues(alpha: 0.1),
              shape: BoxShape.circle,
            ),
            child: Icon(
              Icons.library_music_rounded,
              size: 60,
              color: colorScheme.primary.withValues(alpha: 0.5),
            ),
          ),

          const SizedBox(height: 24),

          // 空状态标题
          Text(
            '暂无收藏',
            style: TextStyle(
              fontSize: 20,
              fontWeight: FontWeight.w600,
              color: Colors.grey.shade300,
            ),
          ),

          const SizedBox(height: 8),

          // 空状态描述
          Text(
            '搜索并收藏你喜欢的音乐吧',
            style: TextStyle(fontSize: 14, color: Colors.grey.shade500),
          ),

          const SizedBox(height: 32),

          // 提示文本
          Text(
            '点击底部「搜索」标签开始',
            style: TextStyle(fontSize: 12, color: Colors.grey.shade600),
          ),
        ],
      ),
    );
  }
}
