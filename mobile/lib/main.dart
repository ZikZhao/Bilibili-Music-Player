import 'package:flutter/material.dart';
import 'package:hive_flutter/hive_flutter.dart';
import 'package:media_kit/media_kit.dart';
import 'package:provider/provider.dart';

import 'models/video_model.dart';
import 'providers/library_provider.dart';
import 'providers/player_provider.dart';
import 'providers/search_provider.dart';
import 'ui/home/home_scaffold.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // 初始化 Hive
  await Hive.initFlutter();
  Hive.registerAdapter(VideoModelAdapter());

  // 初始化 media_kit（支持 Windows/Linux/macOS）
  MediaKit.ensureInitialized();

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
        darkTheme: ThemeData(
          useMaterial3: true,
          brightness: Brightness.dark,
          colorScheme: ColorScheme.dark(
            primary: const Color(0xFFFB7299), // Bilibili Pink
            secondary: const Color(0xFF23ADE5), // Bilibili Blue
            surface: const Color(0xFF1A1A1A),
            onSurface: Colors.white,
            surfaceContainerHighest: const Color(0xFF2A2A2A),
          ),
          scaffoldBackgroundColor: const Color(0xFF121212),
          appBarTheme: const AppBarTheme(
            backgroundColor: Color(0xFF1A1A1A),
            elevation: 0,
            centerTitle: true,
          ),
          bottomNavigationBarTheme: const BottomNavigationBarThemeData(
            backgroundColor: Color(0xFF1A1A1A),
            selectedItemColor: Color(0xFFFB7299),
            unselectedItemColor: Colors.grey,
            type: BottomNavigationBarType.fixed,
          ),
          cardTheme: const CardThemeData(
            color: Color(0xFF2A2A2A),
            elevation: 0,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.all(Radius.circular(12)),
            ),
          ),
          inputDecorationTheme: InputDecorationTheme(
            filled: true,
            fillColor: const Color(0xFF2A2A2A),
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(24),
              borderSide: BorderSide.none,
            ),
            contentPadding: const EdgeInsets.symmetric(
              horizontal: 20,
              vertical: 14,
            ),
          ),
          listTileTheme: const ListTileThemeData(iconColor: Colors.white70),
          switchTheme: SwitchThemeData(
            thumbColor: WidgetStateProperty.resolveWith((states) {
              if (states.contains(WidgetState.selected)) {
                return const Color(0xFFFB7299);
              }
              return Colors.grey;
            }),
            trackColor: WidgetStateProperty.resolveWith((states) {
              if (states.contains(WidgetState.selected)) {
                return const Color(0xFFFB7299).withOpacity(0.5);
              }
              return Colors.grey.withOpacity(0.3);
            }),
          ),
        ),
        home: const HomeScaffold(),
      ),
    );
  }
}
