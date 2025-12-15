import 'dart:async';
import 'dart:io';

import 'package:audio_service/audio_service.dart';
import 'package:flutter/foundation.dart';
import '../player/media_player_adapter.dart';

import '../api/bilibili_client.dart';
import '../models/video_model.dart';
import '../services/cache_manager.dart';

/// B 站音频播放处理器
///
/// 继承自 [BaseAudioHandler]，实现后台播放、播放列表管理等功能
class BilibiliAudioHandler extends BaseAudioHandler with SeekHandler {
  /// media_kit 播放器适配器实例
  final MediaPlayerAdapter _player = MediaPlayerAdapter();

  /// B 站 API 客户端
  final BilibiliClient _client = BilibiliClient();

  /// 播放列表
  final List<VideoModel> _playlist = [];

  /// 当前播放索引
  int _currentIndex = -1;

  /// 渐变暂停定时器
  Timer? _fadeTimer;

  /// 渐变暂停时长
  static const Duration _fadeDuration = Duration(milliseconds: 500);

  /// 是否正在渐变
  bool _isFading = false;

  /// B 站请求头
  static const Map<String, String> _bilibiliHeaders = {
    'User-Agent':
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
    'Referer': 'https://www.bilibili.com/',
  };

  /// 是否已经开始播放过（用于过滤初始化时的 completed 状态）
  bool _hasStartedPlaying = false;

  BilibiliAudioHandler() {
    _init();
  }

  /// 初始化播放器监听
  void _init() {
    // 监听播放 / 处理状态变化并广播给 audio_service
    _player.processingStateStream.listen((state) {
      _player.processingState = state;
      _broadcastState();

      if (state == ProcessingState.completed && _hasStartedPlaying) {
        _handlePlaybackCompleted();
      }
    });

    _player.playingStream.listen((playing) {
      _player.playing = playing;
      if (playing) {
        _hasStartedPlaying = true;
      }
      _broadcastState();
    });

    // 更新位置与时长以便 UI / audio_service 可以读取
    _player.positionStream.listen((_) => _broadcastState());
    _player.durationStream.listen((_) => _broadcastState());
  }

  /// 获取当前播放器实例（供 UI 使用）
  MediaPlayerAdapter get player => _player;

  /// 获取当前播放列表
  List<VideoModel> get playlist => List.unmodifiable(_playlist);

  /// 获取当前播放索引
  int get currentIndex => _currentIndex;

  /// 获取当前播放的视频
  VideoModel? get currentVideo {
    if (_currentIndex >= 0 && _currentIndex < _playlist.length) {
      return _playlist[_currentIndex];
    }
    return null;
  }

  /// 是否有下一首
  bool get hasNext => _currentIndex < _playlist.length - 1;

  /// 是否有上一首
  bool get hasPrevious => _currentIndex > 0;

  /// 播放指定视频
  ///
  /// 这是主要入口方法。实现 Cache-First 策略：
  /// 1. 首先检查本地缓存，有缓存直接播放（无网络请求）
  /// 2. 无缓存时才触发 API 初始化和网络请求
  Future<void> playVideo(VideoModel video) async {
    try {
      debugPrint('[AudioHandler] 播放: ${video.title}');

      // 检查播放列表中是否已存在该视频
      final existingIndex = _playlist.indexWhere((v) => v.bvid == video.bvid);

      if (existingIndex == -1) {
        // 视频不在列表中，添加到末尾
        _playlist.add(video);
        _currentIndex = _playlist.length - 1;
      } else if (_currentIndex != existingIndex) {
        // 视频在列表中，但当前索引不匹配，需要更新
        _currentIndex = existingIndex;
      }
      // else: 索引已正确，无需更新

      // 使用播放列表中当前索引的视频，确保播放正确的歌曲
      final targetVideo = _playlist[_currentIndex];

      // 先更新 MediaItem（无时长），让通知栏立即显示
      _updateMediaItem(targetVideo);

      // ========== Cache-First Strategy ==========
      // Step 1: 首先检查本地缓存（无网络请求！）
      final cachedPath = await CacheManager.instance.getAudioPath(
        targetVideo.bvid,
      );

      Duration? duration;
      bool isLocal = false;

      if (cachedPath != null) {
        // ========== Cache Hit: 直接播放本地文件 ==========
        debugPrint('[AudioHandler] 缓存命中，直接播放: $cachedPath');

        final modelDuration = targetVideo.durationSeconds > 0
            ? Duration(seconds: targetVideo.durationSeconds)
            : null;

        duration = await _player.setAudioSourceFile(
          cachedPath,
          durationTag: modelDuration,
        );
        isLocal = true;

        if (modelDuration != null) {
          _updateMediaItem(targetVideo, duration: modelDuration);
        }
      } else {
        // ========== Cache Miss: 需要网络请求 ==========
        // 此时才触发 API 初始化（Cookie/WBI）
        debugPrint('[AudioHandler] 缓存未命中，从网络加载...');

        // 获取视频详情（会触发 _ensureInitialized）
        final detail = await _client.fetchVideoInfo(targetVideo.bvid);
        final videoDuration = Duration(seconds: detail.duration);

        // 更新播放列表中的视频信息，使用正确的时长
        _playlist[_currentIndex] = targetVideo.copyWith(
          duration: detail.formattedDuration,
        );

        // 更新 MediaItem（带时长）
        _updateMediaItem(_playlist[_currentIndex], duration: videoDuration);

        // 获取播放地址
        final playUrl = await _client.fetchPlayUrl(detail.bvid, detail.cid);

        // 使用网络 URI（注意：若需要特殊 headers，请先用 CacheManager 下载到本地再播放）
        duration = await _player.setAudioSourceUri(
          playUrl.url,
          durationTag: videoDuration,
        );

        // 后台下载缓存（Fire and forget）
        CacheManager.instance.downloadInBackground(
          playUrl.url,
          targetVideo.bvid,
        );
      }
      debugPrint(
        '[AudioHandler] 音频源加载完成，时长: $duration，播放器状态: ${_player.processingState}',
      );

      await Future.delayed(const Duration(milliseconds: 100));

      _hasStartedPlaying = true;

      debugPrint('[AudioHandler] 准备播放，当前 playing 状态: ${_player.playing}');
      await _player.play();
      debugPrint('[AudioHandler] 已调用 play()，新 playing 状态: ${_player.playing}');
    } catch (e, stack) {
      debugPrint('[AudioHandler] 播放失败: $e');
      debugPrint('[AudioHandler] Stack: $stack');
      rethrow;
    }
  }

  /// 更新 MediaItem（通知栏信息）
  void _updateMediaItem(VideoModel video, {Duration? duration}) {
    mediaItem.add(
      MediaItem(
        id: video.bvid,
        title: video.title,
        artist: video.author,
        artUri: Uri.parse(video.cover),
        duration: duration,
      ),
    );
  }

  /// 广播播放状态
  void _broadcastState() {
    final playing = _player.playing;
    final processingState = _player.processingState;

    playbackState.add(
      playbackState.value.copyWith(
        controls: [
          MediaControl.skipToPrevious,
          if (playing) MediaControl.pause else MediaControl.play,
          MediaControl.stop,
          MediaControl.skipToNext,
        ],
        systemActions: const {
          MediaAction.seek,
          MediaAction.seekForward,
          MediaAction.seekBackward,
          MediaAction.skipToPrevious,
          MediaAction.skipToNext,
          MediaAction.play,
          MediaAction.pause,
          MediaAction.stop,
        },
        androidCompactActionIndices: const [0, 1, 3],
        processingState: const {
          ProcessingState.idle: AudioProcessingState.idle,
          ProcessingState.loading: AudioProcessingState.loading,
          ProcessingState.buffering: AudioProcessingState.buffering,
          ProcessingState.ready: AudioProcessingState.ready,
          ProcessingState.completed: AudioProcessingState.completed,
        }[processingState]!,
        playing: playing,
        updatePosition: _player.position,
        bufferedPosition: _player.bufferedPosition,
        speed: _player.speed,
        queueIndex: _currentIndex,
      ),
    );
  }

  /// 处理播放完成
  void _handlePlaybackCompleted() {
    if (hasNext) {
      skipToNext();
    } else {
      // 播放列表结束，停止播放
      debugPrint('[AudioHandler] 播放列表已结束');
    }
  }

  // ========== BaseAudioHandler 方法实现 ==========

  @override
  Future<void> play() async {
    if (_isFading) {
      _cancelFade();
    }
    await _player.play();
  }

  @override
  Future<void> pause() async {
    await _player.pause();
  }

  @override
  Future<void> stop() async {
    await _player.stop();
  }

  @override
  Future<void> seek(Duration position) async {
    await _player.seek(position);
  }

  @override
  Future<void> skipToNext() async {
    if (!hasNext) {
      debugPrint('[AudioHandler] 没有下一首');
      return;
    }

    _currentIndex++;
    await playVideo(_playlist[_currentIndex]);
  }

  @override
  Future<void> skipToPrevious() async {
    // 如果播放超过 3 秒，则重新播放当前歌曲
    if (_player.position.inSeconds > 3) {
      await _player.seek(Duration.zero);
      return;
    }

    if (!hasPrevious) {
      debugPrint('[AudioHandler] 没有上一首');
      return;
    }

    _currentIndex--;
    await playVideo(_playlist[_currentIndex]);
  }

  @override
  Future<void> setSpeed(double speed) async {
    await _player.setSpeed(speed);
  }

  // ========== 自定义方法 ==========

  /// 渐变暂停
  ///
  /// 在 [_fadeDuration] 内将音量从 1.0 降到 0.0，然后暂停
  Future<void> pauseWithFade() async {
    if (_isFading || !_player.playing) return;

    _isFading = true;
    const steps = 10;
    final stepDuration = _fadeDuration ~/ steps;
    var currentStep = 0;

    _fadeTimer = Timer.periodic(stepDuration, (timer) async {
      currentStep++;
      final volume = 1.0 - (currentStep / steps);

      if (currentStep >= steps) {
        timer.cancel();
        await _player.pause();
        await _player.setVolume(1.0); // 恢复音量
        _isFading = false;
        debugPrint('[AudioHandler] 渐变暂停完成');
      } else {
        await _player.setVolume(volume.clamp(0.0, 1.0));
      }
    });
  }

  /// 取消渐变
  void _cancelFade() {
    _fadeTimer?.cancel();
    _fadeTimer = null;
    _isFading = false;
    _player.setVolume(1.0);
  }

  /// 清空播放列表
  void clearPlaylist() {
    _playlist.clear();
    _currentIndex = -1;
    queue.add([]);
  }

  /// 设置播放列表
  ///
  /// 清空当前列表，添加新列表，并设置起始索引。
  /// 这是同步操作，会立即更新 [currentVideo]。
  void setPlaylist(List<VideoModel> videos, {int startIndex = 0}) {
    _playlist.clear();
    _playlist.addAll(videos);

    // 立即设置当前索引，让 UI 可以显示歌曲信息
    if (videos.isNotEmpty && startIndex >= 0 && startIndex < videos.length) {
      _currentIndex = startIndex;
      _updateMediaItem(videos[startIndex]);
    } else {
      _currentIndex = videos.isEmpty ? -1 : 0;
      if (videos.isNotEmpty) {
        _updateMediaItem(videos[0]);
      }
    }

    _updateQueue();
    debugPrint('[AudioHandler] 设置播放列表: ${videos.length} 首, 起始: $startIndex');
  }

  /// 添加到播放列表
  void addToPlaylist(VideoModel video) {
    if (!_playlist.any((v) => v.bvid == video.bvid)) {
      _playlist.add(video);
      _updateQueue();
    }
  }

  /// 从播放列表移除
  void removeFromPlaylist(int index) {
    if (index < 0 || index >= _playlist.length) return;

    _playlist.removeAt(index);

    if (index < _currentIndex) {
      _currentIndex--;
    } else if (index == _currentIndex) {
      // 当前播放的被移除
      if (_playlist.isEmpty) {
        _currentIndex = -1;
        stop();
      } else if (_currentIndex >= _playlist.length) {
        _currentIndex = _playlist.length - 1;
      }
    }

    _updateQueue();
  }

  /// 更新队列
  void _updateQueue() {
    queue.add(
      _playlist
          .map(
            (v) => MediaItem(
              id: v.bvid,
              title: v.title,
              artist: v.author,
              artUri: Uri.parse(v.cover),
            ),
          )
          .toList(),
    );
  }

  /// 跳转到指定索引
  Future<void> skipToIndex(int index) async {
    if (index < 0 || index >= _playlist.length) return;

    _currentIndex = index;
    await playVideo(_playlist[index]);
  }

  /// 释放资源
  Future<void> dispose() async {
    _cancelFade();
    await _player.dispose();
  }
}
