import 'package:audio_service/audio_service.dart';
import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../providers/player_provider.dart';

/// 播放列表底部弹出组件
///
/// 统一了 MiniPlayer 和沉浸式播放器的播放列表样式
class PlaylistSheet extends StatelessWidget {
  const PlaylistSheet({super.key});

  @override
  Widget build(BuildContext context) {
    // 不再使用 Consumer，而是直接获取 Provider 实例并使用 StreamBuilder
    final playerProvider = Provider.of<PlayerProvider>(context, listen: false);
    final colorScheme = Theme.of(context).colorScheme;

    return StreamBuilder<List<MediaItem>>(
      stream: playerProvider.queueStream,
      builder: (context, queueSnapshot) {
        final playlist = queueSnapshot.data ?? [];

        return StreamBuilder<MediaItem?>(
          stream: playerProvider.mediaItemStream,
          builder: (context, mediaSnapshot) {
            final currentItem = mediaSnapshot.data;
            // 计算当前索引
            final currentIndex = playlist.indexWhere(
              (item) => item.id == currentItem?.id,
            );

            return DraggableScrollableSheet(
              initialChildSize: 0.6,
              minChildSize: 0.3,
              maxChildSize: 0.9,
              builder: (context, scrollController) {
                return Container(
                  decoration: BoxDecoration(
                    color: colorScheme.surface,
                    borderRadius: const BorderRadius.vertical(
                      top: Radius.circular(20),
                    ),
                  ),
                  child: Column(
                    children: [
                      // 拖拽指示器
                      Container(
                        margin: const EdgeInsets.symmetric(vertical: 12),
                        width: 40,
                        height: 4,
                        decoration: BoxDecoration(
                          color: Colors.grey.shade600,
                          borderRadius: BorderRadius.circular(2),
                        ),
                      ),

                      // 标题栏
                      Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 20),
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            Row(
                              children: [
                                Text(
                                  '播放列表',
                                  style: TextStyle(
                                    fontSize: 18,
                                    fontWeight: FontWeight.bold,
                                    color: colorScheme.primary,
                                  ),
                                ),
                                const SizedBox(width: 8),
                                Text(
                                  '(${playlist.length})',
                                  style: TextStyle(
                                    fontSize: 14,
                                    color: Colors.grey.shade400,
                                  ),
                                ),
                              ],
                            ),
                            TextButton.icon(
                              onPressed: () {
                                playerProvider.clearPlaylist();
                                Navigator.of(context).pop();
                              },
                              icon: const Icon(Icons.delete_outline, size: 18),
                              label: const Text('清空'),
                              style: TextButton.styleFrom(
                                foregroundColor: Colors.grey.shade400,
                              ),
                            ),
                          ],
                        ),
                      ),

                      const SizedBox(height: 8),

                      // 播放列表
                      Expanded(
                        child: playlist.isEmpty
                            ? const Center(
                                child: Text(
                                  '播放列表为空',
                                  style: TextStyle(color: Colors.grey),
                                ),
                              )
                            : ListView.builder(
                                controller: scrollController,
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 8,
                                ),
                                itemCount: playlist.length,
                                itemBuilder: (context, index) {
                                  final item = playlist[index];
                                  final isCurrentTrack = index == currentIndex;

                                  return ListTile(
                                    leading: SizedBox(
                                      width: 48,
                                      height: 48,
                                      child: Stack(
                                        alignment: Alignment.center,
                                        children: [
                                          Opacity(
                                            opacity: isCurrentTrack ? 0.4 : 1.0,
                                            child: ClipRRect(
                                              borderRadius: BorderRadius.circular(
                                                4,
                                              ),
                                              child: CachedNetworkImage(
                                                imageUrl:
                                                    item.artUri?.toString() ??
                                                        '',
                                                width: 48,
                                                height: 48,
                                                fit: BoxFit.cover,
                                                placeholder: (context, url) =>
                                                    Container(
                                                      color: Colors.grey.shade800,
                                                    ),
                                                errorWidget: (context, url, error) =>
                                                    Container(
                                                      color: Colors.grey.shade800,
                                                    ),
                                              ),
                                            ),
                                          ),
                                          if (isCurrentTrack)
                                            Icon(
                                              Icons.equalizer_rounded,
                                              color: colorScheme.primary,
                                              size: 24,
                                            ),
                                        ],
                                      ),
                                    ),
                                    title: Text(
                                      item.title,
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis,
                                      style: TextStyle(
                                        color: isCurrentTrack
                                            ? colorScheme.primary
                                            : Colors.white,
                                        fontWeight: isCurrentTrack
                                            ? FontWeight.bold
                                            : FontWeight.normal,
                                      ),
                                    ),
                                    subtitle: Text(
                                      item.artist ?? '',
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis,
                                      style: TextStyle(
                                        color: isCurrentTrack
                                            ? colorScheme.primary.withOpacity(
                                                0.7,
                                              )
                                            : Colors.grey.shade500,
                                      ),
                                    ),
                                    onTap: () {
                                      Navigator.of(context).pop();
                                      playerProvider.skipToIndex(index);
                                    },
                                    trailing: IconButton(
                                      icon: const Icon(
                                        Icons.close,
                                        size: 20,
                                        color: Colors.grey,
                                      ),
                                      onPressed: () {
                                        playerProvider.removeFromPlaylist(
                                          index,
                                        );
                                      },
                                    ),
                                  );
                                },
                              ),
                      ),
                    ],
                  ),
                );
              },
            );
          },
        );
      },
    );
  }
}
