import 'dart:ui';

import 'package:audio_video_progress_bar/audio_video_progress_bar.dart';
import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/video_model.dart';
import '../../providers/player_provider.dart';

/// 播放模式
enum PlayMode {
  /// 列表循环
  loop,

  /// 单曲循环
  single,

  /// 随机播放
  shuffle,
}

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
  /// 播放模式
  PlayMode _playMode = PlayMode.loop;

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
  void _togglePlayMode() {
    setState(() {
      switch (_playMode) {
        case PlayMode.loop:
          _playMode = PlayMode.single;
        case PlayMode.single:
          _playMode = PlayMode.shuffle;
        case PlayMode.shuffle:
          _playMode = PlayMode.loop;
      }
    });

    final modeName = switch (_playMode) {
      PlayMode.loop => '列表循环',
      PlayMode.single => '单曲循环',
      PlayMode.shuffle => '随机播放',
    };

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text('已切换至 $modeName'),
        behavior: SnackBarBehavior.floating,
        duration: const Duration(seconds: 1),
      ),
    );
  }

  /// 获取播放模式图标
  IconData _getPlayModeIcon() {
    return switch (_playMode) {
      PlayMode.loop => Icons.repeat_rounded,
      PlayMode.single => Icons.repeat_one_rounded,
      PlayMode.shuffle => Icons.shuffle_rounded,
    };
  }

  @override
  Widget build(BuildContext context) {
    return Consumer<PlayerProvider>(
      builder: (context, playerProvider, child) {
        // 优先使用 Provider 中的当前视频，回退到初始视频
        // 这解决了 Android 上 Provider 状态同步延迟的问题
        final video = playerProvider.currentVideo ?? widget.initialVideo;

        if (video == null) {
          return _buildEmptyState(context);
        }

        // 使用 DraggableScrollableSheet 实现可拖拽的全屏效果
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
                child: Scaffold(
                  extendBodyBehindAppBar: true,
                  appBar: _buildAppBar(context),
                  body: Stack(
                    fit: StackFit.expand,
                    children: [
                      // 模糊背景
                      _buildBlurredBackground(video.cover),

                      // 主体内容
                      SafeArea(
                        child: _buildContent(context, playerProvider, video),
                      ),
                    ],
                  ),
                ),
              ),
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

  /// 构建 AppBar
  PreferredSizeWidget _buildAppBar(BuildContext context) {
    return AppBar(
      backgroundColor: Colors.transparent,
      elevation: 0,
      leading: IconButton(
        icon: const Icon(Icons.keyboard_arrow_down_rounded, size: 32),
        onPressed: () => Navigator.of(context).pop(),
      ),
      title: const Text(
        '正在播放',
        style: TextStyle(fontSize: 14, fontWeight: FontWeight.w500),
      ),
      centerTitle: true,
      actions: [
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
          _buildControlButtons(playerProvider),

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

        // 如果正在下载，使用下载进度作为缓冲进度
        Duration buffered = positionData.bufferedPosition;
        if (playerProvider.isDownloading &&
            positionData.duration.inMilliseconds > 0) {
          buffered = Duration(
            milliseconds:
                (positionData.duration.inMilliseconds *
                        playerProvider.downloadProgress)
                    .toInt(),
          );
        }

        return ProgressBar(
          progress: positionData.position,
          buffered: buffered,
          total: positionData.duration,
          onSeek: playerProvider.seek,
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
  Widget _buildControlButtons(PlayerProvider playerProvider) {
    final colorScheme = Theme.of(context).colorScheme;
    final isPlaying = playerProvider.isPlaying;
    final isLoading = playerProvider.state == AppPlayerState.loading;
    final isDownloading = playerProvider.isDownloading;

    // 显示加载状态：正在加载或正在下载但尚未开始播放
    final showLoadingIndicator = isLoading || (isDownloading && !isPlaying);

    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceEvenly,
      children: [
        // 播放模式
        IconButton(
          icon: Icon(_getPlayModeIcon()),
          iconSize: 24,
          color: _playMode == PlayMode.loop
              ? Colors.grey.shade400
              : colorScheme.primary,
          onPressed: _togglePlayMode,
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
                    await playerProvider.pauseWithFade();
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
                            value: playerProvider.downloadProgress,
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
                          '${(playerProvider.downloadProgress * 100).toInt()}%',
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
    final playlist = playerProvider.playlist;
    final currentIndex = playerProvider.currentIndex;
    final colorScheme = Theme.of(context).colorScheme;

    return Container(
      height: MediaQuery.of(context).size.height * 0.6,
      decoration: BoxDecoration(
        color: colorScheme.surface,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(20)),
      ),
      child: Column(
        children: [
          // 拖拽指示器
          Container(
            margin: const EdgeInsets.only(top: 12),
            width: 40,
            height: 4,
            decoration: BoxDecoration(
              color: Colors.grey.shade600,
              borderRadius: BorderRadius.circular(2),
            ),
          ),

          // 标题栏
          Padding(
            padding: const EdgeInsets.all(16),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  '播放列表 (${playlist.length})',
                  style: const TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.bold,
                  ),
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

          const Divider(height: 1),

          // 列表
          Expanded(
            child: playlist.isEmpty
                ? const Center(
                    child: Text('播放列表为空', style: TextStyle(color: Colors.grey)),
                  )
                : ListView.builder(
                    itemCount: playlist.length,
                    itemBuilder: (context, index) {
                      final video = playlist[index];
                      final isCurrentlyPlaying = index == currentIndex;

                      return ListTile(
                        leading: ClipRRect(
                          borderRadius: BorderRadius.circular(4),
                          child: CachedNetworkImage(
                            imageUrl: video.cover,
                            width: 48,
                            height: 32,
                            fit: BoxFit.cover,
                          ),
                        ),
                        title: Text(
                          video.title,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            color: isCurrentlyPlaying
                                ? colorScheme.primary
                                : null,
                            fontWeight: isCurrentlyPlaying
                                ? FontWeight.bold
                                : null,
                          ),
                        ),
                        subtitle: Text(
                          video.author,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            fontSize: 12,
                            color: Colors.grey.shade500,
                          ),
                        ),
                        trailing: isCurrentlyPlaying
                            ? Icon(
                                Icons.volume_up_rounded,
                                color: colorScheme.primary,
                                size: 20,
                              )
                            : IconButton(
                                icon: const Icon(Icons.close, size: 18),
                                color: Colors.grey.shade500,
                                onPressed: () {
                                  playerProvider.removeFromPlaylist(index);
                                },
                              ),
                        onTap: () {
                          playerProvider.skipToIndex(index);
                          Navigator.of(context).pop();
                        },
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}
