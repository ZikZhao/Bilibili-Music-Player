import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../providers/player_provider.dart';
import '../pages/audio_player_page.dart';

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
    return Consumer<PlayerProvider>(
      builder: (context, playerProvider, child) {
        final video = playerProvider.currentVideo;

        // 没有播放内容时不显示
        if (video == null) {
          return const SizedBox.shrink();
        }

        final colorScheme = Theme.of(context).colorScheme;
        final isPlaying = playerProvider.isPlaying;
        final isLoading = playerProvider.state == AppPlayerState.loading;

        return GestureDetector(
          onTap: () => _openFullPlayer(context, video),
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
                    _buildAlbumArt(video.cover),

                    // 标题和作者
                    Expanded(
                      child: Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 12),
                        child: Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              video.title,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(
                                fontSize: 14,
                                fontWeight: FontWeight.w500,
                              ),
                            ),
                            const SizedBox(height: 2),
                            Text(
                              video.author,
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
                await playerProvider.pauseWithFade();
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
      builder: (context) => _PlaylistSheet(playerProvider: playerProvider),
    );
  }
}

/// 播放列表底部弹出
class _PlaylistSheet extends StatelessWidget {
  final PlayerProvider playerProvider;

  const _PlaylistSheet({required this.playerProvider});

  @override
  Widget build(BuildContext context) {
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

              // 标题
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 20),
                child: Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Text(
                      '播放列表',
                      style: TextStyle(
                        fontSize: 18,
                        fontWeight: FontWeight.bold,
                        color: colorScheme.primary,
                      ),
                    ),
                    Text(
                      '${playlist.length} 首',
                      style: TextStyle(
                        fontSize: 14,
                        color: Colors.grey.shade400,
                      ),
                    ),
                  ],
                ),
              ),

              const SizedBox(height: 8),

              // 播放列表
              Expanded(
                child: ListView.builder(
                  controller: scrollController,
                  padding: const EdgeInsets.symmetric(horizontal: 8),
                  itemCount: playlist.length,
                  itemBuilder: (context, index) {
                    final video = playlist[index];
                    final isCurrentTrack = index == currentIndex;

                    return ListTile(
                      leading: ClipRRect(
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
                      trailing: isCurrentTrack
                          ? Icon(
                              Icons.equalizer_rounded,
                              color: colorScheme.primary,
                            )
                          : null,
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
  }
}
