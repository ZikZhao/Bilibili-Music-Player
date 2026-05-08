import 'package:flutter/material.dart';

void main() {
  runApp(const BiliMusicApp());
}

// --- Theme Constants ---
const Color rose400 = Color(0xFFFB7185);
const Color rose100 = Color(0xFFFFE4E6);
const Color rose500 = Color(0xFFF43F5E);
const Color slate50 = Color(0xFFF8FAFC);
const Color slate100 = Color(0xFFF1F5F9);
const Color slate200 = Color(0xFFE2E8F0);
const Color slate300 = Color(0xFFCBD5E1);
const Color slate400 = Color(0xFF94A3B8);
const Color slate500 = Color(0xFF64748B);
const Color slate600 = Color(0xFF475569);
const Color slate800 = Color(0xFF1E293B);

// --- Mock Data ---
class Track {
  final int id;
  final String title;
  final String artist;
  final String duration;
  final String cover;

  Track(this.id, this.title, this.artist, this.duration, this.cover);
}

final List<Track> mockCollection = [
  Track(
    1,
    "超高清4k音质无损【官方MV】[超时空辉夜姬] 星降る海",
    "夏レモン",
    "04:13",
    "https://images.unsplash.com/photo-1618331835717-801e976710b2?auto=format&fit=crop&q=80&w=200&h=120",
  ),
  Track(
    2,
    "“能遇上这首歌，算你有本事……” | 《願い～あの頃のキミへ～》",
    "Kumorine",
    "05:41",
    "https://images.unsplash.com/photo-1518609878373-06d740f60d8b?auto=format&fit=crop&q=80&w=200&h=120",
  ),
  Track(
    3,
    "若能绽放光芒 | 《四月是你的谎言》OP《光るなら》",
    "JLRS-jayfm",
    "04:10",
    "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&q=80&w=200&h=120",
  ),
  Track(
    4,
    "在百万豪装录音棚大声听Aimer《Ref:rain》【Hi-Res】",
    "JLRS-LeoFM",
    "04:53",
    "https://images.unsplash.com/photo-1598488035139-bdbb2231ce04?auto=format&fit=crop&q=80&w=200&h=120",
  ),
  Track(
    5,
    "Secrets - OneRepublic 共和时代【Hi-Res】",
    "JLRS日落fm",
    "03:47",
    "https://images.unsplash.com/photo-1470225620780-dba8ba36b745?auto=format&fit=crop&q=80&w=200&h=120",
  ),
];

final List<String> searchHistory = [
  "星降之海",
  "若能绽放光芒",
  "祈愿 致那个时候的你",
  "refrain",
  "secrets",
  "sunshine",
  "ring of fortune",
];

class BiliMusicApp extends StatelessWidget {
  const BiliMusicApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'BiliMusic Desktop',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        fontFamily: 'sans-serif',
        scaffoldBackgroundColor: Colors.white,
        colorScheme: ColorScheme.fromSeed(seedColor: rose400),
      ),
      home: const MainLayout(),
    );
  }
}

class MainLayout extends StatefulWidget {
  const MainLayout({super.key});

  @override
  State<MainLayout> createState() => _MainLayoutState();
}

class _MainLayoutState extends State<MainLayout> {
  String activeTab = 'collection';

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Row(
        children: [
          // Sidebar
          Sidebar(
            activeTab: activeTab,
            onTabChanged: (id) => setState(() => activeTab = id),
          ),
          // Main Content + Bottom Player
          Expanded(
            child: Column(
              children: [
                Expanded(
                  child: Stack(
                    children: [
                      if (activeTab == 'collection') const CollectionView(),
                      if (activeTab == 'search') const SearchView(),
                      if (activeTab == 'settings') const SettingsView(),
                    ],
                  ),
                ),
                const BottomPlayer(),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

// --- Components ---

class Sidebar extends StatelessWidget {
  final String activeTab;
  final ValueChanged<String> onTabChanged;

  const Sidebar({
    super.key,
    required this.activeTab,
    required this.onTabChanged,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 256,
      decoration: const BoxDecoration(
        color: slate50,
        border: Border(right: BorderSide(color: slate200)),
      ),
      child: Column(
        children: [
          // Logo Area
          Container(
            height: 80,
            padding: const EdgeInsets.symmetric(horizontal: 32),
            child: Row(
              children: [
                Container(
                  width: 32,
                  height: 32,
                  decoration: BoxDecoration(
                    color: rose400,
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: const Icon(
                    Icons.favorite,
                    color: Colors.white,
                    size: 20,
                  ),
                ),
                const SizedBox(width: 12),
                const Text(
                  'BiliMusic',
                  style: TextStyle(
                    fontSize: 20,
                    fontWeight: FontWeight.bold,
                    letterSpacing: 1.2,
                    color: slate800,
                  ),
                ),
              ],
            ),
          ),
          // Navigation Items
          Expanded(
            child: Padding(
              padding: const EdgeInsets.all(16.0),
              child: Column(
                children: [
                  _NavItem(
                    id: 'search',
                    icon: Icons.search,
                    label: '搜索',
                    isActive: activeTab == 'search',
                    onTap: onTabChanged,
                  ),
                  const SizedBox(height: 8),
                  _NavItem(
                    id: 'collection',
                    icon: Icons.favorite_border,
                    label: '我的收藏',
                    isActive: activeTab == 'collection',
                    onTap: onTabChanged,
                  ),
                  const SizedBox(height: 8),
                  _NavItem(
                    id: 'settings',
                    icon: Icons.settings_outlined,
                    label: '设置',
                    isActive: activeTab == 'settings',
                    onTap: onTabChanged,
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _NavItem extends StatelessWidget {
  final String id;
  final IconData icon;
  final String label;
  final bool isActive;
  final ValueChanged<String> onTap;

  const _NavItem({
    required this.id,
    required this.icon,
    required this.label,
    required this.isActive,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: () => onTap(id),
      borderRadius: BorderRadius.circular(12),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        decoration: BoxDecoration(
          color: isActive ? rose100 : Colors.transparent,
          borderRadius: BorderRadius.circular(12),
        ),
        child: Row(
          children: [
            Icon(icon, size: 20, color: isActive ? rose400 : slate600),
            const SizedBox(width: 12),
            Text(
              label,
              style: TextStyle(
                color: isActive ? rose400 : slate600,
                fontWeight: isActive ? FontWeight.w500 : FontWeight.normal,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class CollectionView extends StatelessWidget {
  const CollectionView({super.key});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(32.0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Container(
                decoration: const BoxDecoration(
                  border: Border(left: BorderSide(color: rose400, width: 4)),
                ),
                padding: const EdgeInsets.only(left: 16),
                child: const Text(
                  '我的收藏',
                  style: TextStyle(
                    fontSize: 28,
                    fontWeight: FontWeight.bold,
                    color: slate800,
                  ),
                ),
              ),
              const Text(
                '= 排序',
                style: TextStyle(
                  fontSize: 14,
                  color: slate500,
                  cursor: SystemMouseCursors.click,
                ),
              ),
            ],
          ),
          const SizedBox(height: 32),
          // Table Header (Simulating CSS Grid with Row + Expanded)
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            decoration: const BoxDecoration(
              border: Border(bottom: BorderSide(color: slate200)),
            ),
            child: const Row(
              children: [
                SizedBox(
                  width: 48,
                  child: Text(
                    '#',
                    textAlign: TextAlign.center,
                    style: TextStyle(color: slate500, fontSize: 14),
                  ),
                ),
                SizedBox(width: 16),
                Expanded(
                  child: Text(
                    '标题',
                    style: TextStyle(color: slate500, fontSize: 14),
                  ),
                ),
                SizedBox(
                  width: 200,
                  child: Text(
                    'UP主',
                    style: TextStyle(color: slate500, fontSize: 14),
                  ),
                ),
                SizedBox(
                  width: 100,
                  child: Row(
                    children: [
                      Icon(Icons.access_time, size: 16, color: slate500),
                      SizedBox(width: 4),
                      Text(
                        '时长',
                        style: TextStyle(color: slate500, fontSize: 14),
                      ),
                    ],
                  ),
                ),
                SizedBox(width: 48),
              ],
            ),
          ),
          const SizedBox(height: 8),
          // List Items
          Expanded(
            child: ListView.builder(
              itemCount: mockCollection.length,
              itemBuilder: (context, index) {
                final track = mockCollection[index];
                return _TrackListItem(track: track, index: index);
              },
            ),
          ),
        ],
      ),
    );
  }
}

class _TrackListItem extends StatefulWidget {
  final Track track;
  final int index;
  const _TrackListItem({required this.track, required this.index});

  @override
  State<_TrackListItem> createState() => _TrackListItemState();
}

class _TrackListItemState extends State<_TrackListItem> {
  bool isHovered = false;

  @override
  Widget build(BuildContext context) {
    return MouseRegion(
      onEnter: (_) => setState(() => isHovered = true),
      onExit: (_) => setState(() => isHovered = false),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        decoration: BoxDecoration(
          color: isHovered ? slate50 : Colors.transparent,
          borderRadius: BorderRadius.circular(12),
        ),
        child: Row(
          children: [
            SizedBox(
              width: 48,
              child: Text(
                '${widget.index + 1}',
                textAlign: TextAlign.center,
                style: TextStyle(
                  color: isHovered ? rose400 : slate400,
                  fontWeight: FontWeight.w500,
                ),
              ),
            ),
            const SizedBox(width: 16),
            Expanded(
              child: Row(
                children: [
                  ClipRRect(
                    borderRadius: BorderRadius.circular(6),
                    child: Image.network(
                      widget.track.cover,
                      width: 96,
                      height: 56,
                      fit: BoxFit.cover,
                      errorBuilder: (_, __, ___) =>
                          Container(width: 96, height: 56, color: slate200),
                    ),
                  ),
                  const SizedBox(width: 16),
                  Expanded(
                    child: Text(
                      widget.track.title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontWeight: FontWeight.w500,
                        color: slate800,
                      ),
                    ),
                  ),
                ],
              ),
            ),
            SizedBox(
              width: 200,
              child: Text(
                widget.track.artist,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(color: slate500, fontSize: 14),
              ),
            ),
            SizedBox(
              width: 100,
              child: Text(
                widget.track.duration,
                style: const TextStyle(
                  color: slate500,
                  fontSize: 14,
                  fontFamily: 'monospace',
                ),
              ),
            ),
            SizedBox(
              width: 48,
              child: isHovered
                  ? const Icon(Icons.more_vert, color: slate400, size: 20)
                  : const SizedBox(),
            ),
          ],
        ),
      ),
    );
  }
}

class SearchView extends StatelessWidget {
  const SearchView({super.key});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(32.0),
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 896), // max-w-4xl
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                decoration: const BoxDecoration(
                  border: Border(left: BorderSide(color: rose400, width: 4)),
                ),
                padding: const EdgeInsets.only(left: 16),
                margin: const EdgeInsets.only(bottom: 32),
                child: const Text(
                  '搜索音乐',
                  style: TextStyle(
                    fontSize: 28,
                    fontWeight: FontWeight.bold,
                    color: slate800,
                  ),
                ),
              ),
              // Search Input
              Container(
                margin: const EdgeInsets.only(bottom: 40),
                decoration: BoxDecoration(
                  color: slate100,
                  borderRadius: BorderRadius.circular(16),
                ),
                child: TextField(
                  decoration: InputDecoration(
                    hintText: '搜索 Bilibili 音乐视频',
                    hintStyle: const TextStyle(color: slate400),
                    prefixIcon: const Icon(Icons.search, color: rose400),
                    border: InputBorder.none,
                    contentPadding: const EdgeInsets.symmetric(
                      horizontal: 16,
                      vertical: 20,
                    ),
                    focusedBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(16),
                      borderSide: const BorderSide(color: rose300, width: 2),
                    ),
                  ),
                ),
              ),
              // History Header
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  const Row(
                    children: [
                      Icon(Icons.search, size: 16, color: slate600),
                      SizedBox(width: 8),
                      Text(
                        '搜索历史',
                        style: TextStyle(
                          color: slate600,
                          fontWeight: FontWeight.w500,
                        ),
                      ),
                    ],
                  ),
                  TextButton(
                    onPressed: () {},
                    child: const Text('清空', style: TextStyle(color: slate400)),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              // History Tags
              Wrap(
                spacing: 12,
                runSpacing: 12,
                children: searchHistory.map((tag) {
                  return Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 16,
                      vertical: 8,
                    ),
                    decoration: BoxDecoration(
                      color: slate100,
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Text(
                      tag,
                      style: const TextStyle(color: slate600, fontSize: 14),
                    ),
                  );
                }).toList(),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class SettingsView extends StatelessWidget {
  const SettingsView({super.key});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(32.0),
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 768), // max-w-3xl
          child: ListView(
            children: [
              Container(
                decoration: const BoxDecoration(
                  border: Border(left: BorderSide(color: rose400, width: 4)),
                ),
                padding: const EdgeInsets.only(left: 16),
                margin: const EdgeInsets.only(bottom: 32),
                child: const Text(
                  '设置',
                  style: TextStyle(
                    fontSize: 28,
                    fontWeight: FontWeight.bold,
                    color: slate800,
                  ),
                ),
              ),

              _buildSectionTitle('播放'),
              _buildSettingsCard([
                _buildSettingsTile(
                  icon: const Text(
                    'HQ',
                    style: TextStyle(
                      fontWeight: FontWeight.bold,
                      fontSize: 10,
                      color: slate600,
                    ),
                  ),
                  title: '音频质量',
                  subtitle: '高 (192K)',
                  trailing: const Icon(Icons.chevron_right, color: slate300),
                ),
                _buildSettingsTile(
                  icon: const Icon(Icons.graphic_eq, size: 18, color: slate600),
                  title: '渐变播放',
                  subtitle: '暂停或开始时音量淡入淡出',
                  trailing: _buildMockSwitch(true),
                ),
                _buildSettingsTile(
                  icon: const Icon(
                    Icons.play_circle_outline,
                    size: 20,
                    color: slate600,
                  ),
                  title: '自动播放',
                  subtitle: '收藏列表点击后自动开始播放',
                  trailing: _buildMockSwitch(true),
                  showBorder: false,
                ),
              ]),
              const SizedBox(height: 40),

              _buildSectionTitle('外观'),
              Container(
                decoration: BoxDecoration(
                  border: Border.all(color: slate200),
                  borderRadius: BorderRadius.circular(32),
                  color: Colors.white,
                ),
                padding: const EdgeInsets.all(4),
                child: Row(
                  children: [
                    Expanded(
                      child: _buildThemeSegment(
                        Icons.desktop_windows,
                        '自动',
                        true,
                      ),
                    ),
                    Expanded(
                      child: _buildThemeSegment(
                        Icons.wb_sunny_outlined,
                        '亮色',
                        false,
                      ),
                    ),
                    Expanded(
                      child: _buildThemeSegment(
                        Icons.nights_stay_outlined,
                        '深色',
                        false,
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 40),

              _buildSectionTitle('数据与存储'),
              _buildSettingsCard([
                _buildSettingsTile(
                  icon: const Icon(
                    Icons.folder_outlined,
                    size: 20,
                    color: slate600,
                  ),
                  title: '清除缓存',
                  subtitle: '已使用: 42.9 MB',
                  trailing: const Icon(Icons.delete_outline, color: rose500),
                  showBorder: false,
                ),
              ]),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildSectionTitle(String title) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Text(
        title,
        style: const TextStyle(
          color: rose400,
          fontSize: 14,
          fontWeight: FontWeight.bold,
        ),
      ),
    );
  }

  Widget _buildSettingsCard(List<Widget> children) {
    return Container(
      decoration: BoxDecoration(
        color: Colors.white,
        border: Border.all(color: slate100),
        borderRadius: BorderRadius.circular(16),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withOpacity(0.02),
            blurRadius: 4,
            offset: const Offset(0, 2),
          ),
        ],
      ),
      child: Column(children: children),
    );
  }

  Widget _buildSettingsTile({
    required Widget icon,
    required String title,
    required String subtitle,
    required Widget trailing,
    bool showBorder = true,
  }) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
      decoration: BoxDecoration(
        border: showBorder
            ? const Border(bottom: BorderSide(color: slate50))
            : null,
      ),
      child: Row(
        children: [
          Container(
            width: 32,
            height: 32,
            decoration: BoxDecoration(
              color: slate100,
              borderRadius: BorderRadius.circular(8),
            ),
            alignment: Alignment.center,
            child: icon,
          ),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: const TextStyle(
                    color: slate800,
                    fontWeight: FontWeight.w500,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  subtitle,
                  style: const TextStyle(color: slate400, fontSize: 12),
                ),
              ],
            ),
          ),
          trailing,
        ],
      ),
    );
  }

  Widget _buildMockSwitch(bool isOn) {
    return Container(
      width: 48,
      height: 24,
      decoration: BoxDecoration(
        color: isOn ? rose400 : slate200,
        borderRadius: BorderRadius.circular(12),
      ),
      alignment: isOn ? Alignment.centerRight : Alignment.centerLeft,
      padding: const EdgeInsets.all(2),
      child: Container(
        width: 20,
        height: 20,
        decoration: const BoxDecoration(
          color: Colors.white,
          shape: BoxShape.circle,
        ),
      ),
    );
  }

  Widget _buildThemeSegment(IconData icon, String label, bool isActive) {
    return Container(
      padding: const EdgeInsets.symmetric(vertical: 8),
      decoration: BoxDecoration(
        color: isActive ? rose400 : Colors.transparent,
        borderRadius: BorderRadius.circular(24),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, size: 16, color: isActive ? Colors.white : slate600),
          const SizedBox(width: 8),
          Text(
            label,
            style: TextStyle(
              color: isActive ? Colors.white : slate600,
              fontWeight: FontWeight.w500,
            ),
          ),
        ],
      ),
    );
  }
}

class BottomPlayer extends StatelessWidget {
  const BottomPlayer({super.key});

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 96,
      decoration: const BoxDecoration(
        color: Colors.white,
        border: Border(top: BorderSide(color: slate200)),
      ),
      padding: const EdgeInsets.symmetric(horizontal: 24),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          // Track Info
          Expanded(
            child: Row(
              children: [
                ClipRRect(
                  borderRadius: BorderRadius.circular(4),
                  child: Image.network(
                    mockCollection[0].cover,
                    width: 64,
                    height: 40,
                    fit: BoxFit.cover,
                    errorBuilder: (_, __, ___) =>
                        Container(width: 64, height: 40, color: slate200),
                  ),
                ),
                const SizedBox(width: 16),
                Expanded(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        mockCollection[0].title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                          fontWeight: FontWeight.bold,
                          fontSize: 14,
                          color: slate800,
                        ),
                      ),
                      Text(
                        mockCollection[0].artist,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontSize: 12, color: slate500),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),

          // Controls
          Expanded(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    IconButton(
                      icon: const Icon(Icons.skip_previous),
                      color: slate400,
                      hoverColor: slate800,
                      onPressed: () {},
                    ),
                    const SizedBox(width: 16),
                    Container(
                      width: 40,
                      height: 40,
                      decoration: BoxDecoration(
                        color: rose400,
                        shape: BoxShape.circle,
                        boxShadow: [
                          BoxShadow(
                            color: rose400.withOpacity(0.3),
                            blurRadius: 8,
                            offset: const Offset(0, 2),
                          ),
                        ],
                      ),
                      child: const Icon(
                        Icons.play_arrow,
                        color: Colors.white,
                        size: 20,
                      ),
                    ),
                    const SizedBox(width: 16),
                    IconButton(
                      icon: const Icon(Icons.skip_next),
                      color: slate400,
                      hoverColor: slate800,
                      onPressed: () {},
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    const Text(
                      '01:24',
                      style: TextStyle(
                        fontSize: 10,
                        color: slate400,
                        fontFamily: 'monospace',
                      ),
                    ),
                    const SizedBox(width: 12),
                    Container(
                      width: 240, // max-w-md approx
                      height: 6,
                      decoration: BoxDecoration(
                        color: slate100,
                        borderRadius: BorderRadius.circular(3),
                      ),
                      child: FractionallySizedBox(
                        alignment: Alignment.centerLeft,
                        widthFactor: 0.33,
                        child: Container(
                          decoration: BoxDecoration(
                            color: rose400,
                            borderRadius: BorderRadius.circular(3),
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(width: 12),
                    const Text(
                      '04:13',
                      style: TextStyle(
                        fontSize: 10,
                        color: slate400,
                        fontFamily: 'monospace',
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),

          // Volume / Actions
          Expanded(
            child: Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: [
                const Icon(Icons.volume_up, size: 20, color: slate400),
                const SizedBox(width: 16),
                Container(
                  width: 96,
                  height: 6,
                  decoration: BoxDecoration(
                    color: slate100,
                    borderRadius: BorderRadius.circular(3),
                  ),
                  child: FractionallySizedBox(
                    alignment: Alignment.centerLeft,
                    widthFactor: 0.66,
                    child: Container(
                      decoration: BoxDecoration(
                        color: slate400,
                        borderRadius: BorderRadius.circular(3),
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
