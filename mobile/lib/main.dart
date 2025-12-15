import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:hive_flutter/hive_flutter.dart';
import 'package:media_kit/media_kit.dart';
import 'package:provider/provider.dart';

import 'models/video_model.dart';
import 'providers/library_provider.dart';
import 'providers/player_provider.dart';
import 'providers/search_provider.dart';
import 'services/cache_manager.dart';
import 'ui/home/home_scaffold.dart';
import 'ui/theme/app_theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // 初始化 Hive
  await Hive.initFlutter();
  Hive.registerAdapter(VideoModelAdapter());

  // 初始化 media_kit（支持 Windows/Linux/macOS）
  MediaKit.ensureInitialized();

  // 初始化缓存管理器
  await CacheManager.instance.initialize();

  // 创建并初始化 LibraryProvider
  final libraryProvider = LibraryProvider();
  await libraryProvider.initialize();

  // 创建并初始化 PlayerProvider
  final playerProvider = PlayerProvider();
  await playerProvider.initialize();

  runApp(
    BilibiliMusicApp(
      libraryProvider: libraryProvider,
      playerProvider: playerProvider,
    ),
  );
}

class BilibiliMusicApp extends StatelessWidget {
  final LibraryProvider libraryProvider;
  final PlayerProvider playerProvider;

  const BilibiliMusicApp({
    super.key,
    required this.libraryProvider,
    required this.playerProvider,
  });

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider(create: (_) => SearchProvider()),
        ChangeNotifierProvider.value(value: libraryProvider),
        ChangeNotifierProvider.value(value: playerProvider),
      ],
      child: MaterialApp(
        title: 'Bilibili Music',
        debugShowCheckedModeBanner: false,
        themeMode: ThemeMode.dark,
        darkTheme: AppTheme.darkTheme,
        home: const HomeScaffold(),
      ),
    );
  }
}
