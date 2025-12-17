import 'package:media_kit/media_kit.dart';

/// 视频播放器单例管理器
///
/// 用于管理全局唯一的视频播放器实例，避免重复创建销毁 Native 资源
class VideoPlayerManager {
  static final VideoPlayerManager _instance = VideoPlayerManager._internal();
  
  factory VideoPlayerManager() => _instance;

  late final Player _player;

  VideoPlayerManager._internal() {
    // 创建全局唯一的 Player 实例
    _player = Player();
  }

  /// 获取 Player 实例
  Player get player => _player;

  /// 停止播放（不销毁实例）
  Future<void> stop() async {
    await _player.stop();
  }

  /// 销毁资源（通常仅在 App 退出时调用）
  Future<void> dispose() async {
    await _player.dispose();
  }
}
