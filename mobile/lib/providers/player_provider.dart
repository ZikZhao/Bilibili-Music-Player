import 'dart:async';

import 'package:audio_service/audio_service.dart';
import 'package:flutter/foundation.dart';
import 'package:just_audio/just_audio.dart' hide PlayerState;
import 'package:rxdart/rxdart.dart';

import '../models/video_model.dart';
import '../player/audio_handler.dart';
import '../services/cache_manager.dart';

/// 播放器状态
enum AppPlayerState {
  /// 空闲
  idle,

  /// 加载中
  loading,

  /// 播放中
  playing,

  /// 暂停
  paused,

  /// 错误
  error,
}

/// 播放进度信息
class PositionData {
  final Duration position;
  final Duration bufferedPosition;
  final Duration duration;

  const PositionData({
    required this.position,
    required this.bufferedPosition,
    required this.duration,
  });

  /// 播放进度百分比 (0.0 - 1.0)
  double get progress {
    if (duration.inMilliseconds == 0) return 0.0;
    return position.inMilliseconds / duration.inMilliseconds;
  }

  /// 缓冲进度百分比 (0.0 - 1.0)
  double get bufferedProgress {
    if (duration.inMilliseconds == 0) return 0.0;
    return bufferedPosition.inMilliseconds / duration.inMilliseconds;
  }
}

/// 播放器状态管理
///
/// 作为 UI 和 [BilibiliAudioHandler] 之间的桥梁
class PlayerProvider extends ChangeNotifier {
  BilibiliAudioHandler? _audioHandler;

  /// 播放器状态
  AppPlayerState _state = AppPlayerState.idle;
  AppPlayerState get state => _state;

  /// 错误信息
  String? _errorMessage;
  String? get errorMessage => _errorMessage;

  /// 是否已初始化
  bool _isInitialized = false;
  bool get isInitialized => _isInitialized;

  /// 下载进度 (0.0 - 1.0)
  double _downloadProgress = 0.0;
  double get downloadProgress => _downloadProgress;

  /// 是否正在下载
  bool _isDownloading = false;
  bool get isDownloading => _isDownloading;

  /// 当前下载的 bvid
  String? _downloadingBvid;

  /// 获取 AudioHandler 实例
  BilibiliAudioHandler? get audioHandler => _audioHandler;

  /// 获取当前播放的视频
  VideoModel? get currentVideo => _audioHandler?.currentVideo;

  /// 获取播放列表
  List<VideoModel> get playlist => _audioHandler?.playlist ?? [];

  /// 获取当前索引
  int get currentIndex => _audioHandler?.currentIndex ?? -1;

  /// 是否正在播放
  bool get isPlaying => _state == AppPlayerState.playing;

  /// 是否有下一首
  bool get hasNext => _audioHandler?.hasNext ?? false;

  /// 是否有上一首
  bool get hasPrevious => _audioHandler?.hasPrevious ?? false;

  /// 订阅列表
  final List<StreamSubscription<dynamic>> _subscriptions = [];

  /// 初始化音频服务
  Future<void> initialize() async {
    if (_isInitialized) return;

    try {
      debugPrint('[PlayerProvider] 初始化音频服务...');

      _audioHandler = await AudioService.init<BilibiliAudioHandler>(
        builder: BilibiliAudioHandler.new,
        config: const AudioServiceConfig(
          androidNotificationChannelId: 'com.bilibili.music.channel.audio',
          androidNotificationChannelName: 'Bilibili Music',
          androidNotificationOngoing: true,
          androidStopForegroundOnPause: true,
        ),
      );

      _setupListeners();
      _isInitialized = true;
      debugPrint('[PlayerProvider] 音频服务初始化成功');
      notifyListeners();
    } catch (e, stack) {
      debugPrint('[PlayerProvider] 初始化失败: $e');
      debugPrint('[PlayerProvider] Stack: $stack');
      _errorMessage = '音频服务初始化失败: $e';
      _state = AppPlayerState.error;
      notifyListeners();
    }
  }

  /// 设置监听器
  void _setupListeners() {
    if (_audioHandler == null) return;

    final player = _audioHandler!.player;

    // 监听播放状态
    _subscriptions.add(
      player.playerStateStream.listen((playerState) {
        _updateState(playerState);
      }),
    );

    // 监听当前歌曲变化
    _subscriptions.add(
      _audioHandler!.mediaItem.listen((_) {
        notifyListeners();
      }),
    );

    // 监听下载进度
    _subscriptions.add(
      CacheManager.instance.progressStream.listen((progress) {
        // 只关注当前播放歌曲的下载进度
        if (progress.bvid == currentVideo?.bvid ||
            progress.bvid == _downloadingBvid) {
          _downloadingBvid = progress.bvid;

          if (progress.isComplete || progress.hasError) {
            _isDownloading = false;
            _downloadProgress = progress.isComplete ? 1.0 : 0.0;
          } else {
            _isDownloading = true;
            _downloadProgress = progress.progress;
          }
          notifyListeners();
        }
      }),
    );
  }

  /// 更新播放状态
  void _updateState(dynamic playerState) {
    final player = _audioHandler?.player;
    if (player == null) return;

    final processingState = player.processingState;
    final playing = player.playing;

    if (processingState == ProcessingState.loading ||
        processingState == ProcessingState.buffering) {
      _state = AppPlayerState.loading;
    } else if (processingState == ProcessingState.ready) {
      _state = playing ? AppPlayerState.playing : AppPlayerState.paused;
    } else if (processingState == ProcessingState.completed) {
      _state = AppPlayerState.paused;
    } else {
      _state = AppPlayerState.idle;
    }

    notifyListeners();
  }

  /// 获取播放进度流
  ///
  /// 合并 position、bufferedPosition、duration 三个流
  Stream<PositionData> get positionDataStream {
    if (_audioHandler == null) {
      return Stream.value(
        const PositionData(
          position: Duration.zero,
          bufferedPosition: Duration.zero,
          duration: Duration.zero,
        ),
      );
    }

    final player = _audioHandler!.player;

    return Rx.combineLatest3<Duration, Duration, Duration?, PositionData>(
      player.positionStream,
      player.bufferedPositionStream,
      player.durationStream,
      (position, bufferedPosition, duration) => PositionData(
        position: position,
        bufferedPosition: bufferedPosition,
        duration: duration ?? Duration.zero,
      ),
    );
  }

  /// 播放视频
  Future<void> playVideo(VideoModel video) async {
    if (_audioHandler == null) {
      _errorMessage = '播放器未初始化';
      _state = AppPlayerState.error;
      notifyListeners();
      return;
    }

    try {
      _state = AppPlayerState.loading;
      _errorMessage = null;
      notifyListeners();

      await _audioHandler!.playVideo(video);
    } catch (e) {
      _errorMessage = '播放失败: $e';
      _state = AppPlayerState.error;
      notifyListeners();
    }
  }

  /// 播放/暂停切换
  Future<void> togglePlayPause() async {
    if (_audioHandler == null) return;

    if (isPlaying) {
      await _audioHandler!.pause();
    } else {
      await _audioHandler!.play();
    }
  }

  /// 播放
  Future<void> play() async {
    await _audioHandler?.play();
  }

  /// 暂停
  Future<void> pause() async {
    await _audioHandler?.pause();
  }

  /// 渐变暂停
  Future<void> pauseWithFade() async {
    await _audioHandler?.pauseWithFade();
  }

  /// 停止
  Future<void> stop() async {
    await _audioHandler?.stop();
  }

  /// 跳转
  Future<void> seek(Duration position) async {
    await _audioHandler?.seek(position);
  }

  /// 下一首
  Future<void> skipToNext() async {
    await _audioHandler?.skipToNext();
  }

  /// 上一首
  Future<void> skipToPrevious() async {
    await _audioHandler?.skipToPrevious();
  }

  /// 跳转到指定索引
  Future<void> skipToIndex(int index) async {
    await _audioHandler?.skipToIndex(index);
  }

  /// 设置播放列表
  ///
  /// 清空当前列表，添加新列表，并设置起始索引。
  /// 这是同步操作，会立即更新 [currentVideo]。
  void setPlaylist(List<VideoModel> videos, {int startIndex = 0}) {
    _audioHandler?.setPlaylist(videos, startIndex: startIndex);
    notifyListeners();
  }

  /// 添加到播放列表
  void addToPlaylist(VideoModel video) {
    _audioHandler?.addToPlaylist(video);
    notifyListeners();
  }

  /// 从播放列表移除
  void removeFromPlaylist(int index) {
    _audioHandler?.removeFromPlaylist(index);
    notifyListeners();
  }

  /// 清空播放列表
  void clearPlaylist() {
    _audioHandler?.clearPlaylist();
    notifyListeners();
  }

  @override
  void dispose() {
    for (final subscription in _subscriptions) {
      subscription.cancel();
    }
    _subscriptions.clear();
    _audioHandler?.dispose();
    super.dispose();
  }
}
