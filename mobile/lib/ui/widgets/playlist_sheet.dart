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
    return Consumer<PlayerProvider>(
      builder: (context, playerProvider, child) {
        final colorScheme = Theme.of(context).colorScheme;
        final playlist = playerProvider.playlist;
        final currentIndex = playerProvider.currentIndex;

        return DraggableScrollableSheet(
          initialChildSize: 0.6,
          minChildSize: 0.3,
          maxChildSize: 0.9,
          builder: (context, scrollController) {
            return Container(
              decoration: BoxDecoration(
                color: colorScheme.surface,
                borderRadius: const BorderRadius.vertical(top: Radius.circular(20)),
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
                            padding: const EdgeInsets.symmetric(horizontal: 8),
                            itemCount: playlist.length,
                            itemBuilder: (context, index) {
                              final video = playlist[index];
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
                                          borderRadius: BorderRadius.circular(4),
                                          child: CachedNetworkImage(
                                            imageUrl: video.cover,
                                            width: 48,
                                            height: 48,
                                            fit: BoxFit.cover,
                                            placeholder: (context, url) =>
                                                Container(color: Colors.grey.shade800),
                                            errorWidget: (context, url, error) =>
                                                Container(color: Colors.grey.shade800),
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
                                  video.title,
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
                                  video.author,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: TextStyle(
                                    color: isCurrentTrack
                                        ? colorScheme.primary.withOpacity(0.7)
                                        : Colors.grey.shade500,
                                  ),
                                ),
                                onTap: () {
                                  Navigator.of(context).pop();
                                  playerProvider.skipToIndex(index);
                                },
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
  }
}
