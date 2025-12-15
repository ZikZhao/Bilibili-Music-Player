import 'dart:async';

import 'package:audio_service/audio_service.dart';
import 'package:flutter/foundation.dart';
import 'package:media_kit/media_kit.dart';

import '../api/bilibili_client.dart';
import '../models/video_model.dart';
import '../services/cache_manager.dart';

/// B 站音频播放处理器
///
/// 继承自 [BaseAudioHandler]，实现后台播放、播放列表管理等功能
class BilibiliAudioHandler extends BaseAudioHandler with SeekHandler {
  /// media_kit Player 实例（单一事实来源）
  final Player _player = Player();

  /// 内部维护的播放/缓冲/时长状态，用于构造 playbackState
  Duration _lastPosition = Duration.zero;
  Duration _lastBuffered = Duration.zero;
  Duration? _lastDuration;

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

  /// Note: headers moved to callers that need them (e.g., video pages)

  /// 是否已经开始播放过（用于过滤初始化时的 completed 状态）
  bool _hasStartedPlaying = false;

  BilibiliAudioHandler() {
    _init();
  }

  /// 初始化播放器监听：使用 media_kit 的 streams 响应式地更新 playbackState
  void _init() {
    // playing
    _player.streams.playing.listen((playing) {
      if (playing) _hasStartedPlaying = true;
      _broadcastState();
    }, onError: (_) {});

    // position
    _player.streams.position.listen((pos) {
      _lastPosition = pos;
      _broadcastState();
    }, onError: (_) {});

    // buffer / buffered position
    _player.streams.buffer.listen((b) {
      _lastBuffered = b;
      _broadcastState();
    }, onError: (_) {});

    // duration
    _player.streams.duration.listen((d) {
      _lastDuration = d;
      _broadcastState();
    }, onError: (_) {});
  }

  /// 获取当前播放器实例（供 UI 使用）
  Player get player => _player;

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

      if (cachedPath != null) {
        // ========== Cache Hit: 直接播放本地文件 ==========
        debugPrint('[AudioHandler] 缓存命中，直接播放: $cachedPath');

        final modelDuration = targetVideo.durationSeconds > 0
            ? Duration(seconds: targetVideo.durationSeconds)
            : null;

        // 使用 media_kit 直接打开本地文件
        await _player.open(Media(cachedPath));
        duration = modelDuration;

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
        // 打开网络 URI（若需要特殊 headers，请预先缓存到本地）
        await _player.open(Media(playUrl.url));
        duration = videoDuration;

        // 后台下载缓存（Fire and forget）
        CacheManager.instance.downloadInBackground(
          playUrl.url,
          targetVideo.bvid,
        );
      }
      debugPrint('[AudioHandler] 音频源加载完成，时长: $duration');

      // 启动播放
      await _player.play();
    } catch (e, stack) {
      debugPrint('[AudioHandler] 播放失败: $e');
      debugPrint('[AudioHandler] Stack: $stack');
      rethrow;
    }
  }

  /// 更新 MediaItem（通知栏信息）
  void _updateMediaItem(VideoModel video, {Duration? duration}) {
    // 如果没有传入 duration，尝试从 VideoModel 解析
    final effectiveDuration = duration ??
        (video.durationSeconds > 0
            ? Duration(seconds: video.durationSeconds)
            : null);
    
    debugPrint('[AudioHandler] _updateMediaItem - BVID: ${video.bvid}, Duration Arg: $duration, VideoModel Secs: ${video.durationSeconds}, Effective: $effectiveDuration');

    mediaItem.add(
      MediaItem(
        id: video.bvid,
        title: video.title,
        artist: video.author,
        artUri: Uri.parse(video.cover),
        duration: effectiveDuration,
      ),
    );
  }

  /// 广播播放状态
  void _broadcastState() {
    final playing = _player.state.playing;

    // 计算 processingState：尽量依据位置和 playing 推断
    AudioProcessingState proc;
    if (_lastDuration != null &&
        _lastDuration! > Duration.zero &&
        _lastPosition >= _lastDuration! &&
        _hasStartedPlaying) {
      proc = AudioProcessingState.completed;
    } else if (playing) {
      proc = AudioProcessingState.ready;
    } else if (_lastDuration == null) {
      proc = AudioProcessingState.loading;
    } else {
      proc = AudioProcessingState.ready;
    }

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
        processingState: proc,
        playing: playing,
        updatePosition: _lastPosition,
        bufferedPosition: _lastBuffered,
        speed: 1.0,
        queueIndex: _currentIndex,
      ),
    );
  }

  // playback completion handled via streams/state

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
    if (_lastPosition.inSeconds > 3) {
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
    try {
      await _player.setRate(speed);
    } catch (_) {}
  }

  // ========== 自定义方法 ==========

  /// 渐变暂停
  ///
  /// 在 [_fadeDuration] 内将音量从 1.0 降到 0.0，然后暂停
  Future<void> pauseWithFade() async {
    if (_isFading || !_player.state.playing) return;

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
        try {
          await _player.setVolume(1.0);
        } catch (_) {}
        _isFading = false;
        debugPrint('[AudioHandler] 渐变暂停完成');
      } else {
        try {
          await _player.setVolume(volume.clamp(0.0, 1.0));
        } catch (_) {}
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
    try {
      await _player.dispose();
    } catch (_) {}
  }
}
