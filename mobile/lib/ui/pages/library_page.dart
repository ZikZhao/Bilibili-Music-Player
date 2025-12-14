import 'package:flutter/material.dart';

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
      body: _buildEmptyState(colorScheme),
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

          // 跳转按钮
          FilledButton.icon(
            onPressed: () {
              // TODO: 跳转到搜索页
            },
            icon: const Icon(Icons.search_rounded),
            label: const Text('去搜索'),
            style: FilledButton.styleFrom(
              backgroundColor: colorScheme.primary,
              foregroundColor: Colors.white,
              padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 12),
            ),
          ),
        ],
      ),
    );
  }
}
