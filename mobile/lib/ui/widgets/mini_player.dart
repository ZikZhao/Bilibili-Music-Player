import 'package:audio_service/audio_service.dart';
import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../providers/player_provider.dart';
import '../pages/audio_player_page.dart';
import 'playlist_sheet.dart';

/// Mini Player 组件
///
/// 显示在底部导航栏上方，用于快速控制播放
/// 点击可展开全屏播放器
class MiniPlayer extends StatelessWidget {
  const MiniPlayer({super.key});

  /// Mini Player 高度
  static const double height = 64.0;

  @override
  Widget build(BuildContext context) {
    // 使用 read 获取 Provider 实例，避免因 notifyListeners 重建
    // 状态更新由 StreamBuilder 处理
    final playerProvider = context.read<PlayerProvider>();

    return StreamBuilder<MediaItem?>(
      stream: playerProvider.mediaItemStream,
      builder: (context, mediaSnapshot) {
        final mediaItem = mediaSnapshot.data;

        // 没有播放内容时不显示
        if (mediaItem == null) {
          return const SizedBox.shrink();
        }

        return StreamBuilder<PlaybackState>(
          stream: playerProvider.playbackStateStream,
          builder: (context, playbackSnapshot) {
            final playbackState = playbackSnapshot.data;
            final isPlaying = playbackState?.playing ?? false;
            final processingState = playbackState?.processingState;
            final isLoading = processingState == AudioProcessingState.loading ||
                processingState == AudioProcessingState.buffering;
            final colorScheme = Theme.of(context).colorScheme;

            return GestureDetector(
              onTap: () => _openFullPlayer(context, playerProvider.currentVideo),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  // 进度条 - 使用 StreamBuilder 监听播放进度
                  StreamBuilder<PositionData>(
                    stream: playerProvider.positionDataStream,
                    builder: (context, snapshot) {
                      final positionData = snapshot.data;
                      final progress = positionData?.progress ?? 0.0;
                      return LinearProgressIndicator(
                        value: progress,
                        minHeight: 2,
                        backgroundColor: Colors.transparent,
                        valueColor: AlwaysStoppedAnimation<Color>(
                          colorScheme.primary,
                        ),
                      );
                    },
                  ),
                  // 主内容
                  Container(
                    height: height - 2, // 减去进度条高度
                    decoration: BoxDecoration(
                      color: colorScheme.surface,
                      boxShadow: [
                        BoxShadow(
                          color: Colors.black.withOpacity(0.2),
                          blurRadius: 8,
                          offset: const Offset(0, -2),
                        ),
                      ],
                    ),
                    child: Row(
                      children: [
                        // 专辑封面
                        _buildAlbumArt(mediaItem.artUri?.toString() ?? ''),

                        // 标题和作者
                        Expanded(
                          child: Padding(
                            padding: const EdgeInsets.symmetric(horizontal: 12),
                            child: Column(
                              mainAxisAlignment: MainAxisAlignment.center,
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  mediaItem.title,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(
                                    fontSize: 14,
                                    fontWeight: FontWeight.w500,
                                  ),
                                ),
                                const SizedBox(height: 2),
                                Text(
                                  mediaItem.artist ?? '',
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: TextStyle(
                                    fontSize: 12,
                                    color: Colors.grey.shade400,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ),

                        // 播放/暂停按钮
                        _buildPlayPauseButton(
                          context,
                          playerProvider,
                          isPlaying,
                          isLoading,
                        ),

                        // 播放列表按钮
                        IconButton(
                          icon: const Icon(Icons.queue_music_rounded),
                          iconSize: 24,
                          color: Colors.grey.shade400,
                          onPressed: () =>
                              _showPlaylistSheet(context, playerProvider),
                        ),

                        const SizedBox(width: 4),
                      ],
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

  /// 构建专辑封面
  Widget _buildAlbumArt(String coverUrl) {
    return Container(
      width: height,
      height: height,
      padding: const EdgeInsets.all(8),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(8),
        child: CachedNetworkImage(
          imageUrl: coverUrl,
          fit: BoxFit.cover,
          placeholder: (context, url) => Container(
            color: Colors.grey.shade800,
            child: const Icon(Icons.music_note_rounded, color: Colors.grey),
          ),
          errorWidget: (context, url, error) => Container(
            color: Colors.grey.shade800,
            child: const Icon(Icons.music_note_rounded, color: Colors.grey),
          ),
        ),
      ),
    );
  }

  /// 构建播放/暂停按钮
  Widget _buildPlayPauseButton(
    BuildContext context,
    PlayerProvider playerProvider,
    bool isPlaying,
    bool isLoading,
  ) {
    final colorScheme = Theme.of(context).colorScheme;

    return IconButton(
      icon: isLoading
          ? SizedBox(
              width: 24,
              height: 24,
              child: CircularProgressIndicator(
                strokeWidth: 2,
                color: colorScheme.primary,
              ),
            )
          : Icon(isPlaying ? Icons.pause_rounded : Icons.play_arrow_rounded),
      iconSize: 32,
      color: colorScheme.primary,
      onPressed: isLoading
          ? null
          : () async {
              if (isPlaying) {
                await playerProvider.pause();
              } else {
                await playerProvider.play();
              }
            },
    );
  }

  /// 打开全屏播放器
  void _openFullPlayer(BuildContext context, dynamic video) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useRootNavigator: true,
      backgroundColor: Colors.transparent,
      builder: (context) => AudioPlayerPage(initialVideo: video),
    );
  }

  /// 显示播放列表
  void _showPlaylistSheet(BuildContext context, PlayerProvider playerProvider) {
    showModalBottomSheet<void>(
      context: context,
      backgroundColor: Colors.transparent,
      isScrollControlled: true,
      builder: (context) => const PlaylistSheet(),
    );
  }
}
