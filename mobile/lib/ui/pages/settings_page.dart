import 'package:flutter/material.dart';
import 'package:hive_flutter/hive_flutter.dart';

import '../../services/cache_manager.dart';
import '../widgets/bili_app_bar.dart';

/// 设置页面
///
/// 包含播放、外观、数据存储等设置项
class SettingsPage extends StatefulWidget {
  const SettingsPage({super.key});

  @override
  State<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends State<SettingsPage> {
  // 播放设置（Mock）
  bool _autoPlay = true;
  bool _enableFade = true;
  String _streamQuality = '高 (192K)';

  // 外观设置
  String _themeMode = 'system';

  // 缓存大小
  String _cacheSize = '计算中...';
  bool _isClearing = false;

  @override
  void initState() {
    super.initState();
    _loadCacheSize();
    final settings = Hive.box('settings');
    _enableFade = settings.get('enable_fade', defaultValue: true);
    _themeMode = settings.get('theme_mode', defaultValue: 'system');
  }

  /// 加载缓存大小
  Future<void> _loadCacheSize() async {
    final size = await CacheManager.instance.getFormattedCacheSize();
    if (mounted) {
      setState(() => _cacheSize = size);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: const BiliAppBar(title: '设置'),
      body: CustomScrollView(
        slivers: [
          SliverList(
            delegate: SliverChildListDelegate([
              // ========== 播放设置 ==========
              _buildSectionHeader('播放', colorScheme),
              _buildListTile(
                title: '音频质量',
                subtitle: _streamQuality,
                icon: Icons.high_quality_rounded,
                onTap: () => _showQualityPicker(colorScheme),
              ),
              _buildSwitchTile(
                title: '渐变播放',
                subtitle: '暂停或开始时音量淡入淡出',
                value: _enableFade,
                icon: Icons.graphic_eq_rounded,
                onChanged: (value) {
                  setState(() => _enableFade = value);
                  Hive.box('settings').put('enable_fade', value);
                },
              ),
              _buildSwitchTile(
                title: '自动播放',
                subtitle: '收藏列表点击后自动开始播放',
                value: _autoPlay,
                icon: Icons.play_circle_outline_rounded,
                onChanged: (value) {
                  setState(() => _autoPlay = value);
                },
              ),

              const SizedBox(height: 8),

              // ========== 外观设置 ==========
              _buildSectionHeader('外观', colorScheme),
              Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 16,
                  vertical: 8,
                ),
                child: SizedBox(
                  width: double.infinity,
                  child: SegmentedButton<String>(
                    segments: const [
                      ButtonSegment(
                        value: 'system',
                        label: Text('跟随系统'),
                        icon: Icon(Icons.brightness_auto_rounded),
                      ),
                      ButtonSegment(
                        value: 'light',
                        label: Text('亮色'),
                        icon: Icon(Icons.wb_sunny_rounded),
                      ),
                      ButtonSegment(
                        value: 'dark',
                        label: Text('深色'),
                        icon: Icon(Icons.dark_mode_rounded),
                      ),
                    ],
                    selected: {_themeMode},
                    onSelectionChanged: (Set<String> newSelection) {
                      setState(() {
                        _themeMode = newSelection.first;
                      });
                      Hive.box('settings').put('theme_mode', _themeMode);
                    },
                    style: ButtonStyle(
                      visualDensity: VisualDensity.comfortable,
                      tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                      side: WidgetStateProperty.all(
                        BorderSide(color: colorScheme.outlineVariant),
                      ),
                    ),
                  ),
                ),
              ),

              const SizedBox(height: 8),

              // ========== 数据与存储 ==========
              _buildSectionHeader('数据与存储', colorScheme),
              ListTile(
                leading: const Icon(Icons.folder_rounded),
                title: const Text('清除缓存'),
                subtitle: Text(
                  '已使用: $_cacheSize',
                  style: TextStyle(color: Colors.grey.shade500, fontSize: 12),
                ),
                trailing: _isClearing
                    ? const SizedBox(
                        width: 24,
                        height: 24,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : Icon(
                        Icons.delete_outline_rounded,
                        color: colorScheme.error,
                      ),
                onTap: _isClearing ? null : _showClearCacheDialog,
              ),

              const SizedBox(height: 8),

              // ========== 关于 ==========
              _buildSectionHeader('关于', colorScheme),
              _buildListTile(
                title: '版本',
                subtitle: 'Bilibili Music Player v0.2.0',
                icon: Icons.info_outline_rounded,
                onTap: _showAboutDialog,
              ),
              _buildListTile(
                title: '开源许可',
                subtitle: 'MIT License',
                icon: Icons.code_rounded,
                onTap: () {
                  ScaffoldMessenger.of(
                    context,
                  ).showSnackBar(const SnackBar(content: Text('开源许可页面开发中')));
                },
              ),

              const SizedBox(height: 32),

              // 版权信息
              Center(
                child: Column(
                  children: [
                    Icon(
                      Icons.music_note_rounded,
                      size: 32,
                      color: colorScheme.primary.withValues(alpha: 0.3),
                    ),
                    const SizedBox(height: 8),
                    Text(
                      '© 2024 Bilibili Music Player',
                      style: TextStyle(
                        color: Colors.grey.shade600,
                        fontSize: 12,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      'Made with ♥ using Flutter',
                      style: TextStyle(
                        color: Colors.grey.shade700,
                        fontSize: 11,
                      ),
                    ),
                  ],
                ),
              ),

              const SizedBox(height: 32),
            ]),
          ),
        ],
      ),
    );
  }

  /// 构建分组标题
  Widget _buildSectionHeader(String title, ColorScheme colorScheme) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 24, 16, 8),
      child: Text(
        title,
        style: TextStyle(
          color: colorScheme.primary,
          fontSize: 13,
          fontWeight: FontWeight.w600,
          letterSpacing: 0.5,
        ),
      ),
    );
  }

  /// 构建开关选项
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

  /// 构建普通列表项
  Widget _buildListTile({
    required String title,
    required String subtitle,
    required IconData icon,
    required VoidCallback onTap,
  }) {
    return ListTile(
      leading: Icon(icon),
      title: Text(title),
      subtitle: Text(
        subtitle,
        style: TextStyle(color: Colors.grey.shade500, fontSize: 12),
      ),
      trailing: const Icon(Icons.chevron_right_rounded, color: Colors.grey),
      onTap: onTap,
    );
  }

  /// 显示音质选择器
  void _showQualityPicker(ColorScheme colorScheme) {
    final options = [
      ('低 (64K)', '节省流量'),
      ('中 (132K)', '平衡音质与流量'),
      ('高 (192K)', '最佳音质'),
    ];

    showModalBottomSheet<void>(
      context: context,
      backgroundColor: colorScheme.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(16)),
      ),
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const SizedBox(height: 8),
            Container(
              width: 40,
              height: 4,
              decoration: BoxDecoration(
                color: Colors.grey.shade600,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
            const SizedBox(height: 16),
            Text(
              '选择音频质量',
              style: TextStyle(
                fontSize: 16,
                fontWeight: FontWeight.w600,
                color: colorScheme.onSurface,
              ),
            ),
            const SizedBox(height: 8),
            ...options.map(
              (option) => ListTile(
                title: Text(option.$1),
                subtitle: Text(
                  option.$2,
                  style: TextStyle(fontSize: 12, color: Colors.grey.shade500),
                ),
                trailing: _streamQuality == option.$1
                    ? Icon(Icons.check_rounded, color: colorScheme.primary)
                    : null,
                onTap: () {
                  setState(() => _streamQuality = option.$1);
                  Navigator.pop(context);
                },
              ),
            ),
            const SizedBox(height: 16),
          ],
        ),
      ),
    );
  }

  /// 显示清除缓存确认对话框
  void _showClearCacheDialog() {
    showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('清除缓存'),
        content: Text('确定要清除所有已下载的音频缓存吗？\n\n当前缓存: $_cacheSize'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () async {
              Navigator.pop(context);
              await _clearCache();
            },
            child: const Text('清除'),
          ),
        ],
      ),
    );
  }

  /// 清除缓存
  Future<void> _clearCache() async {
    setState(() => _isClearing = true);

    try {
      await CacheManager.instance.clearAll();
      await _loadCacheSize();

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('缓存已清除', style: TextStyle(color: Colors.white)),
            backgroundColor: Colors.green,
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('清除失败: $e', style: const TextStyle(color: Colors.white)),
            backgroundColor: Colors.red,
          ),
        );
      }
    } finally {
      if (mounted) {
        setState(() => _isClearing = false);
      }
    }
  }

  /// 显示关于对话框
  void _showAboutDialog() {
    showDialog(
      context: context,
      builder: (context) {
        return AlertDialog(
          title: const Text('关于', textAlign: TextAlign.center),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              // Logo
              Icon(
                Icons.music_note_rounded,
                size: 64,
                color: Theme.of(context).colorScheme.primary,
              ),
              const SizedBox(height: 16),

              // App Name
              const Text(
                'Bilibili Music Player',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
              ),

              const SizedBox(height: 8),

              // Version
              Text(
                'v0.2.0',
                style: TextStyle(color: Colors.grey.shade600, fontSize: 14),
              ),

              const SizedBox(height: 16),

              // Description
              const Text(
                '一个基于 Flutter 的第三方 Bilibili 音乐播放器。\n仅供学习交流使用。',
                textAlign: TextAlign.center,
                style: TextStyle(height: 1.5),
              ),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(context).pop(),
              child: const Text('关闭'),
            ),
          ],
        );
      },
    );
  }
}
