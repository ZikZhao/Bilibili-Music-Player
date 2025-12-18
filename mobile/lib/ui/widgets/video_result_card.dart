import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/video_model.dart';
import '../../providers/library_provider.dart';

/// 视频搜索结果卡片
///
/// 展示视频封面、标题、UP主、播放量等信息
class VideoResultCard extends StatelessWidget {
  final VideoModel video;
  final VoidCallback? onTap;

  const VideoResultCard({super.key, required this.video, this.onTap});

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              // 左侧：视频封面
              _buildCover(colorScheme),

              const SizedBox(width: 12),

              // 右侧：视频信息
              Expanded(child: _buildInfo(context, colorScheme)),
            ],
          ),
        ),
      ),
    );
  }

  /// 构建收藏按钮
  Widget _buildFavoriteButton(BuildContext context) {
    return Consumer<LibraryProvider>(
      builder: (context, libraryProvider, child) {
        final isFavorite = libraryProvider.isFavorite(video);
        final colorScheme = Theme.of(context).colorScheme;

        return IconButton(
          onPressed: () => libraryProvider.toggleFavorite(video),
          icon: Icon(
            isFavorite ? Icons.favorite_rounded : Icons.favorite_border_rounded,
            color: isFavorite ? colorScheme.primary : Colors.grey.shade400,
          ),
          style: IconButton.styleFrom(
            tapTargetSize: MaterialTapTargetSize.shrinkWrap,
          ),
        );
      },
    );
  }

  /// 构建视频封面
  Widget _buildCover(ColorScheme colorScheme) {
    return Stack(
      children: [
        // 封面图片
        ClipRRect(
          borderRadius: BorderRadius.circular(8),
          child: CachedNetworkImage(
            imageUrl: video.cover,
            width: 120,
            height: 75, // 16:10 比例，更适合 B 站封面
            memCacheWidth: 300, // 防止列表滚动内存溢出
            fit: BoxFit.cover,
            placeholder: (context, url) => Container(
              width: 120,
              height: 75,
              color: colorScheme.surfaceContainerHighest,
              child: Center(
                child: Icon(
                  Icons.image_outlined,
                  color: colorScheme.onSurface.withValues(alpha: 0.3),
                ),
              ),
            ),
            errorWidget: (context, url, error) => Container(
              width: 120,
              height: 75,
              color: colorScheme.surfaceContainerHighest,
              child: Center(
                child: Icon(
                  Icons.broken_image_outlined,
                  color: colorScheme.error.withValues(alpha: 0.5),
                ),
              ),
            ),
          ),
        ),

        // 时长标签
        Positioned(
          right: 4,
          bottom: 4,
          child: Container(
            padding: const EdgeInsets.only(left: 6, right: 6, top: 1, bottom: 3),
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
    );
  }

  /// 构建视频信息
  Widget _buildInfo(BuildContext context, ColorScheme colorScheme) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // 标题
        Text(
          video.title,
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(
            fontSize: 14,
            fontWeight: FontWeight.w500,
            height: 1.3,
          ),
        ),

        const SizedBox(height: 8),

        // 底部信息区：UP主、播放量、收藏按钮
        Row(
          children: [
            // 信息列
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // UP 主
                  Row(
                    children: [
                      SizedBox(
                        width: 16,
                        child: Icon(
                          Icons.person_rounded,
                          size: 14,
                          color: Colors.grey.shade500,
                        ),
                      ),
                      const SizedBox(width: 4),
                      Expanded(
                        child: Text(
                          video.author,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            fontSize: 12,
                            color: Colors.grey.shade400,
                          ),
                        ),
                      ),
                    ],
                  ),

                  const SizedBox(height: 2),

                  // 播放量
                  Row(
                    children: [
                      SizedBox(
                        width: 16,
                        child: Icon(
                          Icons.play_arrow_rounded,
                          size: 14,
                          color: Colors.grey.shade500,
                        ),
                      ),
                      const SizedBox(width: 4),
                      Text(
                        '${video.formattedPlayCount}播放',
                        style: TextStyle(
                          fontSize: 12,
                          color: Colors.grey.shade500,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),

            // 收藏按钮
            _buildFavoriteButton(context),
          ],
        ),
      ],
    );
  }
}
