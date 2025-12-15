import 'dart:async';

import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:media_kit/media_kit.dart';
import 'package:media_kit_video/media_kit_video.dart';
import 'package:provider/provider.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

import '../../api/bilibili_client.dart';
import '../../models/bilibili_api_exception.dart';
import '../../models/play_url_info.dart';
import '../../models/video_detail_info.dart';
import '../../models/video_model.dart';
import '../../providers/library_provider.dart';
import '../../providers/player_provider.dart';

/// 视频详情页
///
/// 展示视频播放器、视频信息、UP 主信息等
class VideoDetailPage extends StatefulWidget {
  final VideoModel video;

  const VideoDetailPage({super.key, required this.video});

  @override
  State<VideoDetailPage> createState() => _VideoDetailPageState();
}

class _VideoDetailPageState extends State<VideoDetailPage> {
  final BilibiliClient _client = BilibiliClient();

  // 加载状态
  bool _isLoading = true;
  String? _errorMessage;

  // 视频详情
  VideoDetailInfo? _videoDetail;
  PlayUrlInfo? _playUrl;

  // media_kit 播放器
  late final Player _player;
  late final VideoController _videoController;
  
  // 状态订阅
  StreamSubscription? _playingSubscription;

  // 是否展开简介
  bool _isDescExpanded = false;

  @override
  void initState() {
    super.initState();

    // 初始化 media_kit 播放器
    _player = Player();
    _videoController = VideoController(_player);

    // 监听播放状态（控制屏幕常亮及音频焦点）
    _playingSubscription = _player.stream.playing.listen((playing) {
      if (playing) {
        WakelockPlus.enable();
        // 视频开始播放时，暂停全局音乐播放（带渐变）
        // 使用 try-catch 避免在页面销毁时 context 不可用导致的异常
        try {
          if (mounted) {
            context.read<PlayerProvider>().pause();
          }
        } catch (e) {
          debugPrint('暂停背景音乐失败: $e');
        }
      } else {
        WakelockPlus.disable();
      }
    });

    _loadVideoData();
  }

  @override
  void dispose() {
    _playingSubscription?.cancel();
    _player.dispose();
    WakelockPlus.disable();
    super.dispose();
  }

  /// 加载视频数据
  Future<void> _loadVideoData() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      // 1. 获取视频详情（含 cid）
      final detail = await _client.fetchVideoInfo(widget.video.bvid);

      setState(() {
        _videoDetail = detail;
      });

      // 2. 获取播放地址（视频流 + 音频流）
      final playUrl = await _client.fetchPlayUrl(
        detail.bvid,
        detail.cid,
        audioOnly: false, // 获取完整视频（MP4 格式，视频+音频合一）
      );

      setState(() {
        _playUrl = playUrl;
      });

      // 3. 设置播放源（关键：必须设置 headers）
      final httpHeaders = {
        'User-Agent':
            'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
        'Referer': 'https://www.bilibili.com/',
      };

      // MP4 格式包含视频+音频，直接播放即可
      await _player.open(
        Media(playUrl.url, httpHeaders: httpHeaders),
        play: true, // 自动播放
      );

      setState(() {
        _isLoading = false;
      });
    } on BilibiliApiException catch (e) {
      setState(() {
        _isLoading = false;
        _errorMessage = e.message;
      });
    } catch (e) {
      setState(() {
        _isLoading = false;
        _errorMessage = '加载失败: $e';
      });
    }
  }

  /// 切换收藏状态
  Future<void> _toggleFavorite() async {
    final libraryProvider = context.read<LibraryProvider>();
    final isNowFavorite = await libraryProvider.toggleFavorite(widget.video);

    if (!mounted) return;

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          isNowFavorite
              ? '已添加「${widget.video.title}」到收藏'
              : '已从收藏中移除「${widget.video.title}」',
        ),
        backgroundColor: Theme.of(context).colorScheme.primary,
        behavior: SnackBarBehavior.floating,
        duration: const Duration(seconds: 2),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: SafeArea(
        child: Column(
          children: [
            // 播放器区域
            _buildPlayerSection(),

            // 视频信息区域
            Expanded(child: _buildInfoSection()),
          ],
        ),
      ),

      // 底部操作栏
      bottomNavigationBar: _buildBottomBar(),
    );
  }

  /// 构建播放器区域
  Widget _buildPlayerSection() {
    return AspectRatio(
      aspectRatio: 16 / 9,
      child: Container(color: Colors.black, child: _buildPlayerContent()),
    );
  }

  Widget _buildPlayerContent() {
    // 加载中
    if (_isLoading) {
      return Stack(
        alignment: Alignment.center,
        children: [
          _buildCoverPlaceholder(),
          Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: Colors.black54,
              borderRadius: BorderRadius.circular(8),
            ),
            child: const Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                CircularProgressIndicator(color: Colors.white),
                SizedBox(height: 12),
                Text('加载中...', style: TextStyle(color: Colors.white)),
              ],
            ),
          ),
        ],
      );
    }

    // 加载失败
    if (_errorMessage != null) {
      return Stack(
        alignment: Alignment.center,
        children: [
          _buildCoverPlaceholder(),
          Container(
            margin: const EdgeInsets.all(24),
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: Colors.black87,
              borderRadius: BorderRadius.circular(8),
            ),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(
                  Icons.error_outline,
                  color: Colors.redAccent,
                  size: 48,
                ),
                const SizedBox(height: 12),
                Text(
                  _errorMessage!,
                  style: const TextStyle(color: Colors.white70),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 16),
                ElevatedButton.icon(
                  onPressed: _loadVideoData,
                  icon: const Icon(Icons.refresh),
                  label: const Text('重试'),
                ),
              ],
            ),
          ),
        ],
      );
    }

    // 播放器 - 使用 media_kit
    return Video(controller: _videoController);
  }

  /// 封面占位图
  Widget _buildCoverPlaceholder() {
    return CachedNetworkImage(
      imageUrl: widget.video.cover,
      fit: BoxFit.cover,
      width: double.infinity,
      height: double.infinity,
      httpHeaders: const {'Referer': 'https://www.bilibili.com/'},
      placeholder: (context, url) => Container(
        color: Colors.grey[900],
        child: const Center(child: CircularProgressIndicator()),
      ),
      errorWidget: (context, url, error) => Container(
        color: Colors.grey[900],
        child: const Icon(Icons.broken_image, color: Colors.white24, size: 64),
      ),
    );
  }

  /// 构建视频信息区域
  Widget _buildInfoSection() {
    final colorScheme = Theme.of(context).colorScheme;
    final detail = _videoDetail;

    return Container(
      color: colorScheme.surface,
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // 返回按钮 + 标题
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                IconButton(
                  icon: const Icon(Icons.arrow_back),
                  onPressed: () => Navigator.of(context).pop(),
                  padding: EdgeInsets.zero,
                  constraints: const BoxConstraints(),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: GestureDetector(
                    onTap: () {
                      setState(() {
                        _isDescExpanded = !_isDescExpanded;
                      });
                    },
                    child: Text(
                      detail?.title ?? widget.video.title,
                      style: const TextStyle(
                        fontSize: 18,
                        fontWeight: FontWeight.bold,
                      ),
                      maxLines: _isDescExpanded ? null : 2,
                      overflow: _isDescExpanded ? null : TextOverflow.ellipsis,
                    ),
                  ),
                ),
              ],
            ),

            const SizedBox(height: 16),

            // UP 主信息
            _buildOwnerRow(detail),

            const SizedBox(height: 16),

            // 数据统计行
            _buildStatRow(detail),

            const SizedBox(height: 16),

            // 简介
            if (detail != null && detail.desc.isNotEmpty) ...[
              const Text(
                '简介',
                style: TextStyle(
                  fontSize: 14,
                  fontWeight: FontWeight.bold,
                  color: Colors.white70,
                ),
              ),
              const SizedBox(height: 8),
              GestureDetector(
                onTap: () {
                  setState(() {
                    _isDescExpanded = !_isDescExpanded;
                  });
                },
                child: Text(
                  detail.desc,
                  style: const TextStyle(
                    fontSize: 14,
                    color: Colors.white60,
                    height: 1.5,
                  ),
                  maxLines: _isDescExpanded ? null : 3,
                  overflow: _isDescExpanded ? null : TextOverflow.ellipsis,
                ),
              ),
              if (!_isDescExpanded && detail.desc.length > 100)
                TextButton(
                  onPressed: () {
                    setState(() {
                      _isDescExpanded = true;
                    });
                  },
                  child: const Text('展开'),
                ),
            ],

            // 画质信息
            if (_playUrl != null) ...[
              const SizedBox(height: 16),
              Row(
                children: [
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 4,
                    ),
                    decoration: BoxDecoration(
                      color: colorScheme.primaryContainer,
                      borderRadius: BorderRadius.circular(4),
                    ),
                    child: Text(
                      _playUrl!.qualityName,
                      style: TextStyle(
                        fontSize: 12,
                        color: colorScheme.onPrimaryContainer,
                      ),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text(
                    _playUrl!.format.toUpperCase(),
                    style: const TextStyle(fontSize: 12, color: Colors.white54),
                  ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }

  /// UP 主信息行
  Widget _buildOwnerRow(VideoDetailInfo? detail) {
    final owner = detail?.owner;
    final colorScheme = Theme.of(context).colorScheme;

    return Row(
      children: [
        // 头像
        CircleAvatar(
          radius: 20,
          backgroundColor: Colors.grey[800],
          backgroundImage: owner != null && owner.face.isNotEmpty
              ? CachedNetworkImageProvider(
                  owner.face,
                  headers: const {'Referer': 'https://www.bilibili.com/'},
                )
              : null,
          child: owner == null || owner.face.isEmpty
              ? const Icon(Icons.person, color: Colors.white54)
              : null,
        ),

        const SizedBox(width: 12),

        // 名字
        Expanded(
          child: Text(
            owner?.name ?? widget.video.author,
            style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w500),
          ),
        ),

        // 关注按钮（样式）
        OutlinedButton(
          onPressed: () {
            ScaffoldMessenger.of(context).showSnackBar(
              const SnackBar(
                content: Text('关注功能需要登录'),
                behavior: SnackBarBehavior.floating,
              ),
            );
          },
          style: OutlinedButton.styleFrom(
            foregroundColor: colorScheme.primary,
            side: BorderSide(color: colorScheme.primary),
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          ),
          child: const Text('+ 关注'),
        ),
      ],
    );
  }

  /// 数据统计行
  Widget _buildStatRow(VideoDetailInfo? detail) {
    final stat = detail?.stat;

    return Row(
      children: [
        // 播放量
        _buildStatItem(
          Icons.play_circle_outline,
          stat?.formattedView ?? widget.video.formattedPlayCount,
        ),
        const SizedBox(width: 24),

        // 弹幕数
        _buildStatItem(
          Icons.subtitles_outlined,
          stat?.formattedDanmaku ?? widget.video.danmakuCount.toString(),
        ),
        const SizedBox(width: 24),

        // 发布时间
        _buildStatItem(Icons.access_time, detail?.formattedPubdate ?? ''),
      ],
    );
  }

  Widget _buildStatItem(IconData icon, String text) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 16, color: Colors.white54),
        const SizedBox(width: 4),
        Text(text, style: const TextStyle(fontSize: 13, color: Colors.white54)),
      ],
    );
  }

  /// 底部操作栏
  Widget _buildBottomBar() {
    final colorScheme = Theme.of(context).colorScheme;
    final libraryProvider = context.watch<LibraryProvider>();
    final isFavorite = libraryProvider.isFavorite(widget.video);

    return Container(
      padding: EdgeInsets.only(
        left: 16,
        right: 16,
        top: 12,
        bottom: MediaQuery.of(context).padding.bottom + 12,
      ),
      decoration: BoxDecoration(
        color: colorScheme.surface,
        border: Border(
          top: BorderSide(
            color: colorScheme.outlineVariant.withValues(alpha: 0.3),
          ),
        ),
      ),
      child: Row(
        children: [
          // 分享按钮
          IconButton(
            onPressed: () {
              ScaffoldMessenger.of(context).showSnackBar(
                const SnackBar(
                  content: Text('分享功能开发中'),
                  behavior: SnackBarBehavior.floating,
                ),
              );
            },
            icon: const Icon(Icons.share_outlined),
          ),

          const Spacer(),

          // 收藏按钮
          FilledButton.icon(
            onPressed: _toggleFavorite,
            icon: Icon(isFavorite ? Icons.favorite : Icons.favorite_border),
            label: Text(isFavorite ? '已收藏' : '添加收藏'),
            style: FilledButton.styleFrom(
              backgroundColor: isFavorite
                  ? colorScheme.secondary
                  : colorScheme.primary,
              foregroundColor: isFavorite
                  ? colorScheme.onSecondary
                  : colorScheme.onPrimary,
              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
            ),
          ),
        ],
      ),
    );
  }
}
