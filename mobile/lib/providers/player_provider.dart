import 'dart:async';

import 'package:audio_service/audio_service.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:rxdart/rxdart.dart';

import '../models/video_model.dart';
import '../player/audio_handler.dart';

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
}

/// 播放器状态管理
///
/// Strict Rule 3: 极其精简，不维护独立状态，直接暴露 AudioHandler 的 Stream
class PlayerProvider extends ChangeNotifier {
  BilibiliAudioHandler? _audioHandler;

  /// 获取 AudioHandler 实例
  BilibiliAudioHandler? get audioHandler => _audioHandler;

  /// 获取当前播放模式
  PlayMode get playMode => _audioHandler?.playMode ?? PlayMode.loop;

  /// 初始化音频服务
  Future<void> initialize() async {
    if (_audioHandler != null) return;

    try {
      debugPrint('[PlayerProvider] 初始化音频服务...');

      // 清理旧的 Notification Channel (Android)
      try {
        const platform = MethodChannel('com.bilibili.music/utils');
        await platform.invokeMethod('deleteNotificationChannel', {
          'channelId': 'com.bilibili.music.channel.audio.v4',
        });
        // 尝试删除其他旧版本
        await platform.invokeMethod('deleteNotificationChannel', {
          'channelId': 'com.bilibili.music.channel.audio.v3',
        });
      } catch (e) {
        debugPrint('[PlayerProvider] Failed to delete old channel: $e');
      }

      _audioHandler = await AudioService.init<BilibiliAudioHandler>(
        builder: BilibiliAudioHandler.new,
        config: const AudioServiceConfig(
          androidNotificationChannelId: 'com.bilibili.music.channel.release.v1',
          androidNotificationChannelName: 'Bilibili Music Player',
          androidNotificationChannelDescription: 'Music playback controls',
          androidStopForegroundOnPause: false,
          androidNotificationIcon: 'mipmap/ic_launcher',
          androidShowNotificationBadge: true,
          // 优化: 限制封面图大小，避免 IPC 传输过大导致崩溃或更新延迟
          artDownscaleWidth: 300,
          artDownscaleHeight: 300,
        ),
      );

      debugPrint('[PlayerProvider] 音频服务初始化成功');
      notifyListeners();
    } catch (e, stack) {
      debugPrint('[PlayerProvider] 初始化失败: $e');
      debugPrint(stack.toString());
    }
  }

  // ========== Getters (Computed from Stream Value) ==========

  /// 是否正在播放
  bool get isPlaying =>
      _audioHandler?.playbackState.valueOrNull?.playing ?? false;

  /// 获取当前播放的视频
  VideoModel? get currentVideo => _audioHandler?.currentVideo;

  /// 获取播放列表
  List<VideoModel> get playlist => _audioHandler?.playlist ?? [];

  /// 获取当前索引
  int get currentIndex => _audioHandler?.currentIndex ?? -1;

  /// 是否有下一首
  bool get hasNext => _audioHandler?.hasNext ?? false;

  /// 是否有上一首
  bool get hasPrevious => _audioHandler?.hasPrevious ?? false;

  // ========== Streams (Reactive) ==========

  /// 播放状态流
  Stream<PlaybackState> get playbackStateStream =>
      _audioHandler?.playbackState ?? Stream.value(PlaybackState());

  /// 当前媒体项流
  Stream<MediaItem?> get mediaItemStream =>
      _audioHandler?.mediaItem ?? Stream.value(null);

  /// 播放列表/队列流
  Stream<List<MediaItem>> get queueStream =>
      _audioHandler?.queue ?? Stream.value([]);

  /// 播放进度流 (Combined)
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

    return Rx.combineLatest3<Duration, Duration, Duration, PositionData>(
      player.stream.position.startWith(player.state.position),
      player.stream.buffer.startWith(player.state.buffer),
      player.stream.duration.startWith(player.state.duration),
      (position, buffered, duration) {
        return PositionData(
          position: position,
          bufferedPosition: buffered,
          duration: duration,
        );
      },
    );
  }

  // ========== Actions (Delegates) ==========

  Future<void> playVideo(VideoModel video) async {
    await _audioHandler?.playVideo(video);
  }

  Future<void> play() async => await _audioHandler?.play();

  Future<void> pause() async => await _audioHandler?.pause();

  Future<void> stop() async => await _audioHandler?.stop();

  Future<void> seek(Duration position) async =>
      await _audioHandler?.seek(position);

  Future<void> skipToNext() async => await _audioHandler?.skipToNext();

  Future<void> skipToPrevious() async => await _audioHandler?.skipToPrevious();

  Future<void> skipToIndex(int index) async =>
      await _audioHandler?.skipToIndex(index);

  /// 切换播放模式
  void cyclePlayMode() {
    _audioHandler?.cyclePlayMode();
  }

  void setPlaylist(List<VideoModel> videos, {int startIndex = 0}) {
    _audioHandler?.setPlaylist(videos, startIndex: startIndex);
  }

  void addToPlaylist(VideoModel video) {
    _audioHandler?.addToPlaylist(video);
  }

  void removeFromPlaylist(int index) {
    _audioHandler?.removeFromPlaylist(index);
  }

  void clearPlaylist() {
    _audioHandler?.clearPlaylist();
  }
}

extension ValueStreamExtension<T> on ValueStream<T> {
  T? get valueOrNull => hasValue ? value : null;
}
