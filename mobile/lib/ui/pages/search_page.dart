import 'package:flutter/material.dart';

class SearchPage extends StatefulWidget {
  const SearchPage({super.key});

  @override
  State<SearchPage> createState() => _SearchPageState();
}

class _SearchPageState extends State<SearchPage> {
  final TextEditingController _searchController = TextEditingController();

  // 占位数据
  final List<Map<String, String>> _placeholderResults = [
    {'title': '周杰伦 - 晴天', 'author': 'JayChou官方频道'},
    {'title': '邓紫棋 - 光年之外', 'author': 'GEM邓紫棋'},
    {'title': 'YOASOBI - 夜に駆ける', 'author': 'YOASOBI Official'},
    {'title': '米津玄師 - Lemon', 'author': 'Kenshi Yonezu'},
    {'title': 'Aimer - 残響散歌', 'author': 'Aimer Official'},
  ];

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Text(
          '搜索音乐',
          style: TextStyle(
            color: colorScheme.primary,
            fontWeight: FontWeight.bold,
          ),
        ),
      ),
      body: Column(
        children: [
          // 搜索框
          Padding(
            padding: const EdgeInsets.all(16),
            child: TextField(
              controller: _searchController,
              decoration: InputDecoration(
                hintText: '搜索 Bilibili 视频...',
                hintStyle: TextStyle(color: Colors.grey.shade500),
                prefixIcon: Icon(
                  Icons.search_rounded,
                  color: colorScheme.primary,
                ),
                suffixIcon: IconButton(
                  icon: const Icon(Icons.clear_rounded),
                  onPressed: () => _searchController.clear(),
                ),
              ),
              onSubmitted: (value) {
                // TODO: 实现搜索逻辑
              },
            ),
          ),

          // 热门搜索标签
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: Row(
              children: [
                Icon(
                  Icons.local_fire_department_rounded,
                  color: colorScheme.secondary,
                  size: 20,
                ),
                const SizedBox(width: 8),
                Text(
                  '热门推荐',
                  style: TextStyle(
                    color: colorScheme.secondary,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
          ),

          const SizedBox(height: 8),

          // 占位列表
          Expanded(
            child: ListView.builder(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              itemCount: _placeholderResults.length,
              itemBuilder: (context, index) {
                final item = _placeholderResults[index];
                return _buildSearchResultItem(item, colorScheme);
              },
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSearchResultItem(
    Map<String, String> item,
    ColorScheme colorScheme,
  ) {
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: ListTile(
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        leading: Container(
          width: 56,
          height: 56,
          decoration: BoxDecoration(
            color: colorScheme.primary.withOpacity(0.2),
            borderRadius: BorderRadius.circular(8),
          ),
          child: Icon(Icons.music_note_rounded, color: colorScheme.primary),
        ),
        title: Text(
          item['title'] ?? '',
          style: const TextStyle(fontWeight: FontWeight.w500),
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        subtitle: Padding(
          padding: const EdgeInsets.only(top: 4),
          child: Text(
            item['author'] ?? '',
            style: TextStyle(color: Colors.grey.shade500, fontSize: 12),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
        ),
        trailing: IconButton(
          icon: Icon(
            Icons.play_circle_filled_rounded,
            color: colorScheme.primary,
            size: 36,
          ),
          onPressed: () {
            // TODO: 实现播放逻辑
          },
        ),
        onTap: () {
          // TODO: 实现详情页跳转
        },
      ),
    );
  }
}
