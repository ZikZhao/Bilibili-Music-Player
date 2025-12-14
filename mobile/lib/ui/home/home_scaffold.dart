import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../providers/player_provider.dart';
import '../pages/library_page.dart';
import '../pages/search_page.dart';
import '../pages/settings_page.dart';
import '../widgets/mini_player.dart';

class HomeScaffold extends StatefulWidget {
  const HomeScaffold({super.key});

  @override
  State<HomeScaffold> createState() => _HomeScaffoldState();
}

class _HomeScaffoldState extends State<HomeScaffold> {
  int _currentIndex = 0;

  final List<Widget> _pages = const [
    SearchPage(),
    LibraryPage(),
    SettingsPage(),
  ];

  void _onTabTapped(int index) {
    setState(() {
      _currentIndex = index;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Consumer<PlayerProvider>(
        builder: (context, playerProvider, child) {
          final hasCurrentVideo = playerProvider.currentVideo != null;

          return Column(
            children: [
              // 主内容区域
              Expanded(
                child: IndexedStack(index: _currentIndex, children: _pages),
              ),

              // Mini Player（只在有播放内容时显示）
              if (hasCurrentVideo) const MiniPlayer(),
            ],
          );
        },
      ),
      bottomNavigationBar: BottomNavigationBar(
        currentIndex: _currentIndex,
        onTap: _onTabTapped,
        items: const [
          BottomNavigationBarItem(
            icon: Icon(Icons.search_rounded),
            activeIcon: Icon(Icons.search_rounded),
            label: '搜索',
          ),
          BottomNavigationBarItem(
            icon: Icon(Icons.library_music_outlined),
            activeIcon: Icon(Icons.library_music_rounded),
            label: '收藏',
          ),
          BottomNavigationBarItem(
            icon: Icon(Icons.settings_outlined),
            activeIcon: Icon(Icons.settings_rounded),
            label: '设置',
          ),
        ],
      ),
    );
  }
}
