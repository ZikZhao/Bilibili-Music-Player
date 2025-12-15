import 'dart:ui';

import 'package:audio_service/audio_service.dart';
import 'package:audio_video_progress_bar/audio_video_progress_bar.dart';
import 'package:cached_network_image/cached_network_image.dart' hide DownloadProgress;
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/video_model.dart';
import '../../player/audio_handler.dart';
import '../../providers/player_provider.dart';
import '../../services/cache_manager.dart';
import '../widgets/playlist_sheet.dart';

/// 音频播放器页面
///
/// 类似 QQ 音乐/Spotify 的沉浸式播放界面
class AudioPlayerPage extends StatefulWidget {
  /// 初始视频（可选）
  ///
  /// 如果提供，页面会立即显示该视频信息，无需等待 Provider 状态同步。
  /// 这解决了 Android 上 AudioService 异步初始化导致的空状态问题。
  final VideoModel? initialVideo;

  const AudioPlayerPage({super.key, this.initialVideo});

  @override
  State<AudioPlayerPage> createState() => _AudioPlayerPageState();
}

class _AudioPlayerPageState extends State<AudioPlayerPage>
    with SingleTickerProviderStateMixin {
  /// 封面动画控制器
  late AnimationController _coverAnimController;

  @override
  void initState() {
    super.initState();
    _coverAnimController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 20),
    )..repeat();
  }

  @override
  void dispose() {
    _coverAnimController.dispose();
    super.dispose();
  }

  /// 切换播放模式
  void _togglePlayMode(PlayerProvider playerProvider) {
    playerProvider.cyclePlayMode();

    final modeName = switch (playerProvider.playMode) {
      PlayMode.loop => '列表循环',
      PlayMode.single => '单曲循环',
      PlayMode.shuffle => '随机播放',
    };

    ScaffoldMessenger.of(context).clearSnackBars();
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text('已切换至 $modeName'),
        behavior: SnackBarBehavior.floating,
        duration: const Duration(seconds: 1),
      ),
    );
  }

  /// 获取播放模式图标
  IconData _getPlayModeIcon(PlayMode mode) {
    return switch (mode) {
      PlayMode.loop => Icons.repeat_rounded,
      PlayMode.single => Icons.repeat_one_rounded,
      PlayMode.shuffle => Icons.shuffle_rounded,
    };
  }

  @override
  Widget build(BuildContext context) {
    final playerProvider = Provider.of<PlayerProvider>(context, listen: false);

    return StreamBuilder<MediaItem?>(
      stream: playerProvider.mediaItemStream,
      builder: (context, mediaSnapshot) {
        final video = playerProvider.currentVideo ?? widget.initialVideo;

        if (video == null) {
          return _buildEmptyState(context);
        }

        return StreamBuilder<PlaybackState>(
          stream: playerProvider.playbackStateStream,
          builder: (context, playbackSnapshot) {
            final playbackState = playbackSnapshot.data;

            return StreamBuilder<DownloadProgress>(
              stream: CacheManager.instance.progressStream,
              builder: (context, downloadSnapshot) {
                // 计算下载状态
                bool isDownloading = false;
                double downloadProgress = 0.0;
                
                if (downloadSnapshot.hasData) {
                  final progress = downloadSnapshot.data!;
                  if (progress.bvid == video.bvid) {
                     if (!progress.isComplete && !progress.hasError) {
                       isDownloading = true;
                       downloadProgress = progress.progress;
                     }
                  }
                }

                return DraggableScrollableSheet(
                  initialChildSize: 1.0,
                  minChildSize: 0.5,
                  maxChildSize: 1.0,
                  expand: false,
                  builder: (context, scrollController) {
                    return Container(
                      decoration: BoxDecoration(
                        color: Theme.of(context).scaffoldBackgroundColor,
                        borderRadius: const BorderRadius.vertical(
                          top: Radius.circular(20),
                        ),
                      ),
                      child: ClipRRect(
                        borderRadius: const BorderRadius.vertical(
                          top: Radius.circular(20),
                        ),
                        child: Stack(
                          fit: StackFit.expand,
                          children: [
                            // Layer 1: 背景图
                            Positioned.fill(
                              child: _buildBlurredBackground(video.cover),
                            ),

                            // Layer 2: 内容层
                            Material(
                              color: Colors.transparent,
                              child: Padding(
                                // 2. 使用上面计算好的 effectiveTopPadding
                                padding: EdgeInsets.only(
                                  top: MediaQueryData.fromView(
                                    View.of(context),
                                  ).padding.top,
                                ),
                                child: Column(
                                  children: [
                                    _buildCustomHeader(context),
                                    Expanded(
                                      child: _buildContent(
                                        context,
                                        playerProvider,
                                        video,
                                        playbackState,
                                        isDownloading,
                                        downloadProgress,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
                    );
                  },
                );
              },
            );
          },
        );
      },
    );
  }

  /// 构建空状态
  Widget _buildEmptyState(BuildContext context) {
    return DraggableScrollableSheet(
      initialChildSize: 1.0,
      minChildSize: 0.5,
      maxChildSize: 1.0,
      expand: false,
      builder: (context, scrollController) {
        return Container(
          decoration: BoxDecoration(
            color: Theme.of(context).scaffoldBackgroundColor,
            borderRadius: const BorderRadius.vertical(top: Radius.circular(20)),
          ),
          child: ClipRRect(
            borderRadius: const BorderRadius.vertical(top: Radius.circular(20)),
            child: Scaffold(
              appBar: AppBar(
                backgroundColor: Colors.transparent,
                elevation: 0,
                leading: IconButton(
                  icon: const Icon(Icons.keyboard_arrow_down_rounded, size: 32),
                  onPressed: () => Navigator.of(context).pop(),
                ),
              ),
              body: const Center(
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Icon(Icons.music_off_rounded, size: 64, color: Colors.grey),
                    SizedBox(height: 16),
                    Text(
                      '暂无播放内容',
                      style: TextStyle(fontSize: 18, color: Colors.grey),
                    ),
                    SizedBox(height: 8),
                    Text(
                      '从收藏或搜索中选择音乐开始播放',
                      style: TextStyle(fontSize: 14, color: Colors.grey),
                    ),
                  ],
                ),
              ),
            ),
          ),
        );
      },
    );
  }

  /// 构建自定义 Header
  Widget _buildCustomHeader(BuildContext context) {
    return Row(
      children: [
        IconButton(
          icon: const Icon(Icons.keyboard_arrow_down_rounded, size: 32),
          onPressed: () => Navigator.of(context).pop(),
        ),
        const Expanded(
          child: Center(
            child: Text(
              '正在播放',
              style: TextStyle(fontSize: 14, fontWeight: FontWeight.w500),
            ),
          ),
        ),
        IconButton(
          icon: const Icon(Icons.more_vert_rounded),
          onPressed: () {
            // TODO: 显示更多选项
          },
        ),
      ],
    );
  }

  /// 构建模糊背景
  Widget _buildBlurredBackground(String coverUrl) {
    return Stack(
      fit: StackFit.expand,
      children: [
        // 封面图片
        CachedNetworkImage(
          imageUrl: coverUrl,
          fit: BoxFit.cover,
          errorWidget: (context, url, error) =>
              Container(color: Colors.grey.shade900),
        ),

        // 高斯模糊
        BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 30, sigmaY: 30),
          child: Container(color: Colors.transparent),
        ),

        // 深色遮罩
        Container(color: Colors.black54),
      ],
    );
  }

  /// 构建主体内容
  Widget _buildContent(
    BuildContext context,
    PlayerProvider playerProvider,
    VideoModel video,
    PlaybackState? playbackState,
    bool isDownloading,
    double downloadProgress,
  ) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 32),
      child: Column(
        children: [
          const Spacer(flex: 1),

          // 封面卡片
          _buildCoverCard(video.cover),

          const SizedBox(height: 40),

          // 信息区
          _buildInfoSection(video),

          const SizedBox(height: 32),

          // 进度条
          _buildProgressBar(playerProvider),

          const SizedBox(height: 24),

          // 控制按钮
          _buildControlButtons(
            playerProvider,
            playbackState,
            isDownloading,
            downloadProgress,
          ),

          const Spacer(flex: 2),
        ],
      ),
    );
  }

  /// 构建封面卡片
  Widget _buildCoverCard(String coverUrl) {
    return Hero(
      tag: 'player_cover',
      child: Container(
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(16),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withOpacity(0.4),
              blurRadius: 30,
              offset: const Offset(0, 15),
            ),
          ],
        ),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(16),
          child: AspectRatio(
            aspectRatio: 16 / 10, // 略微裁剪，介于 16:9 和 4:3 之间
            child: CachedNetworkImage(
              imageUrl: coverUrl,
              fit: BoxFit.cover,
              placeholder: (context, url) => Container(
                color: Colors.grey.shade800,
                child: const Center(
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
              errorWidget: (context, url, error) => Container(
                color: Colors.grey.shade800,
                child: const Icon(
                  Icons.music_note_rounded,
                  size: 64,
                  color: Colors.grey,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  /// 构建信息区
  Widget _buildInfoSection(VideoModel video) {
    return Column(
      children: [
        // 标题
        Text(
          video.title,
          textAlign: TextAlign.center,
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(
            fontSize: 20,
            fontWeight: FontWeight.bold,
            height: 1.3,
          ),
        ),

        const SizedBox(height: 8),

        // UP主
        Text(
          video.author,
          style: TextStyle(fontSize: 14, color: Colors.grey.shade400),
        ),
      ],
    );
  }

  /// 构建进度条
  Widget _buildProgressBar(PlayerProvider playerProvider) {
    return StreamBuilder<PositionData>(
      stream: playerProvider.positionDataStream,
      builder: (context, snapshot) {
        final positionData =
            snapshot.data ??
            const PositionData(
              position: Duration.zero,
              bufferedPosition: Duration.zero,
              duration: Duration.zero,
            );

        // 缓冲进度：仅使用播放器的实际缓冲位置。对于完全本地/已缓存的文件，
        // 如果缓冲位置已达到时长，则显示为完整时长（100%）。
        Duration buffered = positionData.bufferedPosition;
        if (positionData.duration.inMilliseconds > 0 &&
            positionData.bufferedPosition >= positionData.duration) {
          buffered = positionData.duration;
        }

        return ProgressBar(
          progress: positionData.position,
          buffered: buffered,
          total: positionData.duration,
          onSeek: playerProvider.seek,
          // 显示时间文本在进度条两侧，便于用户查看当前时间/总时长
          timeLabelLocation: TimeLabelLocation.sides,
          barHeight: 4,
          baseBarColor: Colors.white.withOpacity(0.2),
          progressBarColor: Theme.of(context).colorScheme.primary,
          bufferedBarColor: Colors.white.withOpacity(0.3),
          thumbColor: Theme.of(context).colorScheme.primary,
          thumbRadius: 6,
          timeLabelTextStyle: TextStyle(
            color: Colors.grey.shade400,
            fontSize: 12,
          ),
        );
      },
    );
  }

  /// 构建控制按钮
  Widget _buildControlButtons(
    PlayerProvider playerProvider,
    PlaybackState? playbackState,
    bool isDownloading,
    double downloadProgress,
  ) {
    final colorScheme = Theme.of(context).colorScheme;
    final isPlaying = playbackState?.playing ?? false;
    final processingState = playbackState?.processingState;
    final isLoading = processingState == AudioProcessingState.loading ||
        processingState == AudioProcessingState.buffering;

    // 显示加载状态：正在加载或正在下载但尚未开始播放
    final showLoadingIndicator = isLoading || (isDownloading && !isPlaying);

    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceEvenly,
      children: [
        // 播放模式
        IconButton(
          icon: Icon(_getPlayModeIcon(playerProvider.playMode)),
          iconSize: 24,
          color: playerProvider.playMode == PlayMode.loop
              ? Colors.grey.shade400
              : colorScheme.primary,
          onPressed: () => _togglePlayMode(playerProvider),
        ),

        // 上一首
        IconButton(
          icon: const Icon(Icons.skip_previous_rounded),
          iconSize: 40,
          color: playerProvider.hasPrevious ? Colors.white : Colors.grey,
          onPressed: playerProvider.hasPrevious
              ? playerProvider.skipToPrevious
              : null,
        ),

        // 播放/暂停
        GestureDetector(
          onTap: isLoading
              ? null
              : () async {
                  if (isPlaying) {
                    await playerProvider.pause();
                  } else {
                    await playerProvider.play();
                  }
                },
          child: Container(
            width: 72,
            height: 72,
            decoration: BoxDecoration(
              color: colorScheme.primary,
              shape: BoxShape.circle,
              boxShadow: [
                BoxShadow(
                  color: colorScheme.primary.withOpacity(0.4),
                  blurRadius: 20,
                  offset: const Offset(0, 8),
                ),
              ],
            ),
            child: showLoadingIndicator
                ? Stack(
                    alignment: Alignment.center,
                    children: [
                      // 下载进度圆环
                      if (isDownloading)
                        SizedBox(
                          width: 52,
                          height: 52,
                          child: CircularProgressIndicator(
                            value: downloadProgress,
                            strokeWidth: 3,
                            color: Colors.white.withOpacity(0.8),
                            backgroundColor: Colors.white.withOpacity(0.2),
                          ),
                        )
                      else
                        const SizedBox(
                          width: 32,
                          height: 32,
                          child: CircularProgressIndicator(
                            strokeWidth: 3,
                            color: Colors.white,
                          ),
                        ),
                      // 下载百分比
                      if (isDownloading)
                        Text(
                          '${(downloadProgress * 100).toInt()}%',
                          style: const TextStyle(
                            color: Colors.white,
                            fontSize: 12,
                            fontWeight: FontWeight.bold,
                          ),
                        ),
                    ],
                  )
                : Icon(
                    isPlaying ? Icons.pause_rounded : Icons.play_arrow_rounded,
                    size: 40,
                    color: Colors.white,
                  ),
          ),
        ),

        // 下一首
        IconButton(
          icon: const Icon(Icons.skip_next_rounded),
          iconSize: 40,
          color: playerProvider.hasNext ? Colors.white : Colors.grey,
          onPressed: playerProvider.hasNext ? playerProvider.skipToNext : null,
        ),

        // 播放列表
        IconButton(
          icon: const Icon(Icons.queue_music_rounded),
          iconSize: 24,
          color: Colors.grey.shade400,
          onPressed: () {
            _showPlaylistSheet(context, playerProvider);
          },
        ),
      ],
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
