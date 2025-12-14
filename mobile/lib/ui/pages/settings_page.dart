import 'package:flutter/material.dart';

class SettingsPage extends StatefulWidget {
  const SettingsPage({super.key});

  @override
  State<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends State<SettingsPage> {
  bool _autoPlay = true;
  bool _backgroundPlay = true;
  bool _highQualityAudio = false;
  bool _saveMobileData = false;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Text(
          '设置',
          style: TextStyle(
            color: colorScheme.primary,
            fontWeight: FontWeight.bold,
          ),
        ),
      ),
      body: ListView(
        children: [
          // 播放设置分组
          _buildSectionHeader('播放设置', colorScheme),
          _buildSwitchTile(
            title: '自动播放',
            subtitle: '搜索结果自动开始播放',
            value: _autoPlay,
            icon: Icons.play_arrow_rounded,
            onChanged: (value) {
              setState(() => _autoPlay = value);
            },
          ),
          _buildSwitchTile(
            title: '后台播放',
            subtitle: '允许在后台继续播放音乐',
            value: _backgroundPlay,
            icon: Icons.phonelink_ring_rounded,
            onChanged: (value) {
              setState(() => _backgroundPlay = value);
            },
          ),

          const Divider(height: 32),

          // 音频设置分组
          _buildSectionHeader('音频设置', colorScheme),
          _buildSwitchTile(
            title: '高音质',
            subtitle: '优先加载高品质音频 (192K)',
            value: _highQualityAudio,
            icon: Icons.high_quality_rounded,
            onChanged: (value) {
              setState(() => _highQualityAudio = value);
            },
          ),
          _buildSwitchTile(
            title: '省流模式',
            subtitle: '移动网络下降低音质',
            value: _saveMobileData,
            icon: Icons.data_saver_on_rounded,
            onChanged: (value) {
              setState(() => _saveMobileData = value);
            },
          ),

          const Divider(height: 32),

          // 其他设置分组
          _buildSectionHeader('其他', colorScheme),
          _buildListTile(
            title: '清除缓存',
            subtitle: '已使用 0 MB',
            icon: Icons.cleaning_services_rounded,
            onTap: () {
              _showClearCacheDialog();
            },
          ),
          _buildListTile(
            title: '关于',
            subtitle: 'Bilibili Music Player v0.1.0',
            icon: Icons.info_outline_rounded,
            onTap: () {
              _showAboutDialog();
            },
          ),

          const SizedBox(height: 32),

          // 版权信息
          Center(
            child: Text(
              '© 2024 Bilibili Music Player',
              style: TextStyle(color: Colors.grey.shade600, fontSize: 12),
            ),
          ),

          const SizedBox(height: 16),
        ],
      ),
    );
  }

  Widget _buildSectionHeader(String title, ColorScheme colorScheme) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
      child: Text(
        title,
        style: TextStyle(
          color: colorScheme.primary,
          fontSize: 14,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }

  Widget _buildSwitchTile({
    required String title,
    required String subtitle,
    required bool value,
    required IconData icon,
    required ValueChanged<bool> onChanged,
  }) {
    return SwitchListTile(
      title: Text(title),
      subtitle: Text(
        subtitle,
        style: TextStyle(color: Colors.grey.shade500, fontSize: 12),
      ),
      value: value,
      onChanged: onChanged,
      secondary: Icon(icon),
    );
  }

  Widget _buildListTile({
    required String title,
    required String subtitle,
    required IconData icon,
    required VoidCallback onTap,
  }) {
    return ListTile(
      title: Text(title),
      subtitle: Text(
        subtitle,
        style: TextStyle(color: Colors.grey.shade500, fontSize: 12),
      ),
      leading: Icon(icon),
      trailing: const Icon(Icons.chevron_right_rounded, color: Colors.grey),
      onTap: onTap,
    );
  }

  void _showClearCacheDialog() {
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('清除缓存'),
        content: const Text('确定要清除所有缓存吗？'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () {
              Navigator.pop(context);
              ScaffoldMessenger.of(
                context,
              ).showSnackBar(const SnackBar(content: Text('缓存已清除')));
            },
            child: const Text('确定'),
          ),
        ],
      ),
    );
  }

  void _showAboutDialog() {
    final colorScheme = Theme.of(context).colorScheme;

    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: Row(
          children: [
            Icon(Icons.music_note_rounded, color: colorScheme.primary),
            const SizedBox(width: 8),
            const Text('关于'),
          ],
        ),
        content: const Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Bilibili Music Player'),
            SizedBox(height: 8),
            Text(
              '一个简洁的 Bilibili 音频播放器\n支持搜索、收藏和后台播放',
              style: TextStyle(color: Colors.grey, fontSize: 14),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('关闭'),
          ),
        ],
      ),
    );
  }
}
