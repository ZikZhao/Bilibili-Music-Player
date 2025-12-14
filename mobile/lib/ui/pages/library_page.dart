import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/video_model.dart';
import '../../providers/library_provider.dart';
import '../../providers/player_provider.dart';
import '../widgets/video_result_card.dart';
import 'audio_player_page.dart';

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
          IconButton(
            icon: Icon(Icons.sort_rounded, color: colorScheme.secondary),
            onPressed: () {
              // TODO: 实现排序功能
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

  Widget _buildFavoritesList(BuildContext context, List<VideoModel> favorites) {
    return ListView.builder(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
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
  void _playFromLibrary(
    BuildContext context,
    List<VideoModel> favorites,
    int startIndex,
  ) {
    final playerProvider = context.read<PlayerProvider>();

    // 清空当前播放列表并添加所有收藏
    playerProvider.clearPlaylist();
    for (final video in favorites) {
      playerProvider.addToPlaylist(video);
    }

    // 播放选中的歌曲
    playerProvider.playVideo(favorites[startIndex]);

    // 跳转到播放器页面
    Navigator.of(context).push(
      MaterialPageRoute<void>(builder: (context) => const AudioPlayerPage()),
    );
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
              color: colorScheme.primary.withOpacity(0.1),
              shape: BoxShape.circle,
            ),
            child: Icon(
              Icons.library_music_rounded,
              size: 60,
              color: colorScheme.primary.withOpacity(0.5),
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
