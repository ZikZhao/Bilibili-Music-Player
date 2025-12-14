import 'dart:async';
import 'dart:io';

import 'package:audio_service/audio_service.dart';
import 'package:flutter/foundation.dart';
import 'package:just_audio/just_audio.dart';

import '../api/bilibili_client.dart';
import '../models/video_model.dart';
import '../services/cache_manager.dart';

/// B 站音频播放处理器
///
/// 继承自 [BaseAudioHandler]，实现后台播放、播放列表管理等功能
class BilibiliAudioHandler extends BaseAudioHandler with SeekHandler {
  /// just_audio 播放器实例
  final AudioPlayer _player = AudioPlayer();

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

  BilibiliAudioHandler() {
    _init();
  }

  /// 初始化播放器监听
  void _init() {
    // 监听播放状态变化
    _player.playbackEventStream.listen(_broadcastState);

    // 监听当前索引变化（用于 ConcatenatingAudioSource）
    _player.currentIndexStream.listen((index) {
      if (index != null && index != _currentIndex && index < _playlist.length) {
        _currentIndex = index;
        _updateMediaItem(_playlist[index]);
      }
    });

    // 监听播放完成
    _player.processingStateStream.listen((state) {
      if (state == ProcessingState.completed) {
        _handlePlaybackCompleted();
      }
    });
  }

  /// 获取当前播放器实例（供 UI 使用）
  AudioPlayer get player => _player;

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
  /// 这是主要入口方法。会获取音频流 URL 并开始播放。
  /// 优先使用本地缓存，无缓存时从网络加载并后台下载缓存。
  Future<void> playVideo(VideoModel video) async {
    try {
      debugPrint('[AudioHandler] 开始播放: ${video.title}');

      // 添加到播放列表（如果不存在）
      final existingIndex = _playlist.indexWhere((v) => v.bvid == video.bvid);
      if (existingIndex == -1) {
        _playlist.add(video);
        _currentIndex = _playlist.length - 1;
      } else {
        _currentIndex = existingIndex;
      }

      // 更新 MediaItem（通知栏显示）
      _updateMediaItem(video);

      // 获取视频详情（包含 cid）
      final detail = await _client.fetchVideoInfo(video.bvid);

      // 检查本地缓存
      final cachedPath = await CacheManager.instance.getAudioPath(video.bvid);
      AudioSource audioSource;

      if (cachedPath != null) {
        // 使用本地缓存（秒加载！）
        debugPrint('[AudioHandler] 使用本地缓存: $cachedPath');
        audioSource = AudioSource.file(
          cachedPath,
          tag: MediaItem(
            id: video.bvid,
            title: video.title,
            artist: video.author,
            duration: Duration(seconds: detail.duration),
            artUri: Uri.parse(video.cover),
          ),
        );
      } else {
        // 从网络加载
        final playUrl = await _client.fetchPlayUrl(detail.bvid, detail.cid);
        debugPrint(
          '[AudioHandler] 获取到播放地址: ${playUrl.url.substring(0, 80)}...',
        );

        // 创建带 Header 的网络音频源
        audioSource = AudioSource.uri(
          Uri.parse(playUrl.url),
          headers: _bilibiliHeaders,
          tag: MediaItem(
            id: video.bvid,
            title: video.title,
            artist: video.author,
            duration: Duration(seconds: detail.duration),
            artUri: Uri.parse(video.cover),
          ),
        );

        // 后台下载缓存（Fire and forget）
        CacheManager.instance.downloadInBackground(playUrl.url, video.bvid);
      }

      // 设置音频源并播放
      await _player.setAudioSource(audioSource);
      await _player.play();

      debugPrint('[AudioHandler] 播放开始');
    } catch (e, stack) {
      debugPrint('[AudioHandler] 播放失败: $e');
      debugPrint('[AudioHandler] Stack: $stack');
      rethrow;
    }
  }

  /// 更新 MediaItem（通知栏信息）
  void _updateMediaItem(VideoModel video) {
    mediaItem.add(
      MediaItem(
        id: video.bvid,
        title: video.title,
        artist: video.author,
        artUri: Uri.parse(video.cover),
      ),
    );
  }

  /// 广播播放状态
  void _broadcastState(PlaybackEvent event) {
    final playing = _player.playing;
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
        },
        androidCompactActionIndices: const [0, 1, 3],
        processingState: const {
          ProcessingState.idle: AudioProcessingState.idle,
          ProcessingState.loading: AudioProcessingState.loading,
          ProcessingState.buffering: AudioProcessingState.buffering,
          ProcessingState.ready: AudioProcessingState.ready,
          ProcessingState.completed: AudioProcessingState.completed,
        }[_player.processingState]!,
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
