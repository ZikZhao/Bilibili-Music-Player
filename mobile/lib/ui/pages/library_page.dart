import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/video_model.dart';
import '../../providers/library_provider.dart';
import '../../providers/player_provider.dart';
import '../widgets/bili_app_bar.dart';

class LibraryPage extends StatefulWidget {
  const LibraryPage({super.key});

  @override
  State<LibraryPage> createState() => _LibraryPageState();
}

class _LibraryPageState extends State<LibraryPage> {
  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: const BiliAppBar(title: '我的收藏'),
      body: Column(
        children: [
          // Control Bar
          _buildControlBar(context, colorScheme),

          // Spacing
          const SizedBox(height: 12),

          // List
          Expanded(
            child: Consumer<LibraryProvider>(
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
          ),
        ],
      ),
    );
  }

  /// 构建控制栏
  Widget _buildControlBar(BuildContext context, ColorScheme colorScheme) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      color: colorScheme.surface,
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          // 左侧：默认文件夹标题
          Text(
            '默认文件夹',
            style: TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.bold,
              color: colorScheme.onSurface,
            ),
          ),

          // 右侧：排序按钮
          Consumer<LibraryProvider>(
            builder: (context, provider, _) {
              return PopupMenuButton<SortOption>(
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
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 4),
                  child: Row(
                    children: [
                      Icon(
                        Icons.sort_rounded,
                        size: 18,
                        color: colorScheme.onSurfaceVariant,
                      ),
                      const SizedBox(width: 4),
                      Text(
                        '排序',
                        style: TextStyle(
                          color: colorScheme.onSurfaceVariant,
                          fontSize: 14,
                          fontWeight: FontWeight.w500,
                        ),
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
        ],
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
      padding: const EdgeInsets.only(bottom: 16),
      itemCount: favorites.length,
      itemBuilder: (context, index) {
        final video = favorites[index];
        return _buildListItem(context, video, favorites, index);
      },
    );
  }

  /// 构建列表项
  Widget _buildListItem(
    BuildContext context,
    VideoModel video,
    List<VideoModel> favorites,
    int index,
  ) {
    return InkWell(
      onTap: () => _playFromLibrary(context, favorites, index),
      child: Container(
        height: 90,
        padding: const EdgeInsets.only(left: 16, right: 0, top: 8, bottom: 8),
        child: Row(
          children: [
            // Leading: 16:9 Image with Stack
            AspectRatio(
              aspectRatio: 16 / 9,
              child: Container(
                clipBehavior: Clip.antiAlias,
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(6),
                  boxShadow: [
                    BoxShadow(
                      color: Colors.black.withValues(alpha: 0.1),
                      blurRadius: 4,
                      offset: const Offset(0, 2),
                    ),
                  ],
                ),
                child: Stack(
                  fit: StackFit.expand,
                  children: [
                    // Layer 1: Image
                    CachedNetworkImage(
                      imageUrl: video.cover,
                      httpHeaders: const {
                        'Referer': 'https://www.bilibili.com/',
                      },
                      fit: BoxFit.cover,
                      placeholder: (context, url) => Container(
                        color: Theme.of(
                          context,
                        ).colorScheme.surfaceContainerHighest,
                      ),
                      errorWidget: (context, url, error) => Container(
                        color: Theme.of(
                          context,
                        ).colorScheme.surfaceContainerHighest,
                        child: const Icon(Icons.broken_image, size: 20),
                      ),
                    ),

                    // Layer 3: Index Number (Bottom-Left)
                    Positioned(
                      left: 4,
                      bottom: 4,
                      child: Container(
                        padding: const EdgeInsets.only(
                          left: 6,
                          right: 6,
                          top: 1,
                          bottom: 3,
                        ),
                        decoration: BoxDecoration(
                          color: Colors.black.withValues(alpha: 0.7),
                          borderRadius: BorderRadius.circular(4),
                        ),
                        child: Text(
                          '${index + 1}',
                          style: const TextStyle(
                            color: Colors.white,
                            fontSize: 11,
                            fontWeight: FontWeight.w500,
                          ),
                        ),
                      ),
                    ),

                    // Layer 4: Duration (Bottom-Right)
                    Positioned(
                      right: 4,
                      bottom: 4,
                      child: Container(
                        padding: const EdgeInsets.only(
                          left: 6,
                          right: 6,
                          top: 1,
                          bottom: 3,
                        ),
                        decoration: BoxDecoration(
                          color: Colors.black.withValues(alpha: 0.7),
                          borderRadius: BorderRadius.circular(4),
                        ),
                        child: Text(
                          video.formattedDuration,
                          style: const TextStyle(
                            color: Colors.white,
                            fontSize: 11,
                            fontWeight: FontWeight.w500,
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(width: 12),

            // Text Column
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  // Title
                  Text(
                    video.title,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.onSurface,
                      fontSize: 15,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                  const SizedBox(height: 4),
                  // Artist
                  Text(
                    video.author,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(color: Colors.grey.shade500, fontSize: 12),
                  ),
                ],
              ),
            ),

            // Trailing: More Icon
            IconButton(
              icon: Icon(
                Icons.more_vert,
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
              onPressed: () {
                // TODO: Show more actions
              },
            ),
          ],
        ),
      ),
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
