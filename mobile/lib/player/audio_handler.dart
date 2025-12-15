import 'dart:async';
import 'dart:math';

import 'package:audio_service/audio_service.dart';
import 'package:flutter/foundation.dart';
import 'package:hive_flutter/hive_flutter.dart';
import 'package:media_kit/media_kit.dart';

import '../api/bilibili_client.dart';
import '../models/play_url_info.dart';
import '../models/video_detail_info.dart';
import '../models/video_model.dart';
import '../services/cache_manager.dart';

/// 播放模式
enum PlayMode {
  /// 列表循环
  loop,

  /// 单曲循环
  single,

  /// 随机播放
  shuffle,
}

/// B 站音频播放处理器
///
/// 继承自 [BaseAudioHandler]，实现后台播放、播放列表管理等功能
class BilibiliAudioHandler extends BaseAudioHandler with SeekHandler {
  /// media_kit Player 实例（单一事实来源）
  ///
  /// Strict Rule 1: 必须且只能持有一个 final Player 实例
  final Player _player = Player();

  /// B 站 API 客户端
  final BilibiliClient _client = BilibiliClient();

  /// 播放列表
  final List<VideoModel> _playlist = [];

  /// 原始播放列表（用于随机播放恢复）
  final List<VideoModel> _originalPlaylist = [];

  /// 当前播放索引
  int _currentIndex = -1;
  
  /// 播放模式
  PlayMode _playMode = PlayMode.loop;

  /// Bilibili headers for media_kit
  static const Map<String, String> _bilibiliHeaders = {
    'User-Agent':
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
    'Referer': 'https://www.bilibili.com/',
  };

  /// 渐变锁 - 已弃用，使用动态状态管理
  // bool _isFading = false;
  
  /// 用户请求的播放状态
  /// 
  /// 如果不为 null，则强制覆盖底层播放器状态用于 UI 显示
  bool? _userRequestedPlaying;
  
  /// 渐变计时器
  Timer? _fadeTimer;

  BilibiliAudioHandler() {
    _init();
  }

  /// 初始化播放器监听
  Future<void> _init() async {
    // Strict Rule 4: 确保初始化时配置 AudioOutput
    // 默认通常是正确的，但为了保险可以显式设置（media_kit 默认自动选择）
    // await _player.setAudioTrack(AudioTrack.auto()); 

    // 监听播放器状态流
    _player.stream.playing.listen((playing) {
      _broadcastState(playing: playing);
    });

    _player.stream.position.listen((position) {
      _broadcastState(position: position);
    });

    _player.stream.duration.listen((duration) {
      _broadcastState(duration: duration);

      // 时长反向同步：将内核探测到的真实时长更新到 MediaItem
      final currentItem = mediaItem.value;
      if (currentItem != null && duration != Duration.zero) {
        // 只有当时长确实改变且有效时才更新，避免死循环或无效更新
        if (currentItem.duration != duration) {
           mediaItem.add(currentItem.copyWith(duration: duration));
        }
      }
    });

    _player.stream.buffer.listen((buffered) {
      _broadcastState(buffered: buffered);
    });

    // 监听播放完成
    _player.stream.completed.listen((completed) {
      if (completed) {
        if (_playMode == PlayMode.single) {
          // 单曲循环模式：重播当前
          seek(Duration.zero);
          play();
        } else if (hasNext || _playMode == PlayMode.loop) {
          // 列表循环或有下一首
          skipToNext();
        } else {
          stop();
          seek(Duration.zero);
        }
      }
    });
    
    // 监听错误
    _player.stream.error.listen((error) {
       debugPrint('[AudioHandler] Player error: $error');
    });

    // 初始化初始状态 (Critical for Android 11+ System Media Control)
    // 确保系统立即知道这是一个活跃的媒体会话
    _broadcastState();
  }

  /// 获取当前播放器实例
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
  Future<void> playVideo(VideoModel video) async {
    try {
      debugPrint('[AudioHandler] 准备播放: ${video.title}');

      // 1. 更新播放列表索引
      final existingIndex = _playlist.indexWhere((v) => v.bvid == video.bvid);
      if (existingIndex == -1) {
        _playlist.add(video);
        _currentIndex = _playlist.length - 1;
      } else {
        _currentIndex = existingIndex;
      }

      final targetVideo = _playlist[_currentIndex];

      // 2. 立即通知 UI 正在加载 (Strict Rule 3: 响应式)
      playbackState.add(playbackState.value.copyWith(
        processingState: AudioProcessingState.loading,
        controls: [MediaControl.stop], // 加载时只显示停止
      ));
      
      // 更新 MediaItem 基础信息
      _updateMediaItem(targetVideo);

      // 3. 获取播放地址（Cache First）
      String? playPath;
      Duration? duration;
      bool isLocal = false;

      // 检查缓存
      final cachedPath = await CacheManager.instance.getAudioPath(targetVideo.bvid);
      
      if (cachedPath != null) {
        debugPrint('[AudioHandler] 缓存命中: $cachedPath');
        playPath = cachedPath;
        isLocal = true;
        if (targetVideo.durationSeconds > 0) {
          duration = Duration(seconds: targetVideo.durationSeconds);
        }
      } else {
        debugPrint('[AudioHandler] 缓存未命中，请求网络资源...');
        // 获取视频详情（为了准确时长和 CID）
        final detail = await _client.fetchVideoInfo(targetVideo.bvid);
        duration = Duration(seconds: detail.duration);
        
        // 更新播放列表中的详细信息
        _playlist[_currentIndex] = targetVideo.copyWith(
          duration: detail.formattedDuration,
        );
        
        // 获取播放 URL
        final playUrl = await _client.fetchPlayUrl(detail.bvid, detail.cid);
        playPath = playUrl.url;
        
        // 触发后台下载
        CacheManager.instance.downloadInBackground(playPath, targetVideo.bvid);
      }

      // 更新 MediaItem 带时长
      _updateMediaItem(_playlist[_currentIndex], duration: duration);

      // Strict Rule 2: 直接调用 _player.open，不要 stop()
      // Strict Rule 2: 确保 HTTP Headers 正确
      if (playPath != null) {
        debugPrint('[AudioHandler] 打开媒体资源: $playPath (Local: $isLocal)');
        
        await _player.open(
          Media(
            playPath,
            httpHeaders: isLocal ? null : _bilibiliHeaders, // 关键：网络请求必须带 Headers
          ),
          play: true, // 自动播放
        );
        
        debugPrint('[AudioHandler] 媒体资源已打开');
      }

    } catch (e, stack) {
      debugPrint('[AudioHandler] 播放失败: $e');
      debugPrint(stack.toString());
      playbackState.add(playbackState.value.copyWith(
        processingState: AudioProcessingState.error,
        errorMessage: e.toString(),
      ));
    }
  }

  /// 更新 MediaItem
  void _updateMediaItem(VideoModel video, {Duration? duration}) {
    final effectiveDuration = duration ??
        (video.durationSeconds > 0
            ? Duration(seconds: video.durationSeconds)
            : null);

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

  /// 获取当前播放模式
  PlayMode get playMode => _playMode;

  /// 切换播放模式
  void cyclePlayMode() {
    switch (_playMode) {
      case PlayMode.loop:
        _playMode = PlayMode.single;
        break;
      case PlayMode.single:
        _playMode = PlayMode.shuffle;
        break;
      case PlayMode.shuffle:
        _playMode = PlayMode.loop;
        break;
    }
    _broadcastState();
  }

  /// 获取播放模式对应的 MediaControl
  MediaControl _getModeControl() {
    // 注意：resource 必须对应 drawable 文件夹下的 xml 文件名
    switch (_playMode) {
      case PlayMode.loop:
        return const MediaControl(
          androidIcon: 'drawable/ic_mode_loop',
          label: 'Loop',
          action: MediaAction.custom,
          customAction: CustomMediaAction(name: 'custom_set_mode'),
        );
      case PlayMode.single:
        return const MediaControl(
          androidIcon: 'drawable/ic_mode_single',
          label: 'Single',
          action: MediaAction.custom,
          customAction: CustomMediaAction(name: 'custom_set_mode'),
        );
      case PlayMode.shuffle:
        return const MediaControl(
          androidIcon: 'drawable/ic_mode_shuffle',
          label: 'Shuffle',
          action: MediaAction.custom,
          customAction: CustomMediaAction(name: 'custom_set_mode'),
        );
    }
  }

  @override
  Future<void> customAction(String name, [Map<String, dynamic>? extras]) async {
    if (name == 'custom_set_mode') {
      cyclePlayMode();
    }
  }

  /// 广播播放状态
  /// 
  /// 将 media_kit 的状态映射到 audio_service 的 PlaybackState
  void _broadcastState({
    bool? playing,
    Duration? position,
    Duration? duration,
    Duration? buffered,
  }) {
    // 优先使用用户请求的状态 (解决渐变时的状态闪烁)
    final isPlaying = _userRequestedPlaying ?? playing ?? _player.state.playing;
    final currentPosition = position ?? _player.state.position;
    final currentBuffered = buffered ?? _player.state.buffer;
    // final totalDuration = duration ?? _player.state.duration;

    // 推断 processingState
    // 注意：media_kit 的 buffering 状态可能需要结合 buffer 进度判断，
    // 但这里简化处理，主要依赖 playing 状态。
    // 如果需要更精确的 buffering 状态，可以监听 _player.stream.buffering
    AudioProcessingState processingState;
    
    // 如果正在加载（通过 playVideo 设置的 loading 状态），保持 loading
    // 直到播放器真正开始播放或缓冲
    if (playbackState.value.processingState == AudioProcessingState.loading && 
        !isPlaying && 
        currentPosition == Duration.zero) {
        processingState = AudioProcessingState.loading;
    } else if (isPlaying) {
      processingState = AudioProcessingState.ready;
    } else if (currentPosition > Duration.zero && !isPlaying) {
      processingState = AudioProcessingState.ready; // 暂停
    } else {
      processingState = AudioProcessingState.idle;
    }

    final modeControl = _getModeControl();

    playbackState.add(
      playbackState.value.copyWith(
        processingState: processingState,
        playing: isPlaying,
        updatePosition: currentPosition,
        bufferedPosition: currentBuffered,
        
        controls: [
          MediaControl.skipToPrevious,
          if (isPlaying) MediaControl.pause else MediaControl.play,
          MediaControl.skipToNext,
          modeControl,
        ],
        
        systemActions: const {
          MediaAction.seek,
          MediaAction.seekForward,
          MediaAction.seekBackward,
          MediaAction.playPause,
          MediaAction.stop,
        },
        androidCompactActionIndices: const [0, 1, 2],
        speed: _player.state.rate,
        queueIndex: _currentIndex,
      ),
    );
  }

  /// 执行带渐变的音量调整
  /// 
  /// [targetVolume] 目标音量 (0.0 - 100.0)
  Future<void> _setVolumeWithFade(double targetVolume) async {
    // 取消之前的渐变任务
    _fadeTimer?.cancel();

    final startVolume = _player.state.volume;
    final delta = (targetVolume - startVolume).abs();
    
    // 如果差异很小，直接设置并结束
    if (delta < 1.0) {
      await _player.setVolume(targetVolume);
      _onFadeComplete(targetVolume);
      return;
    }

    // 计算总时长：增加到 800ms 让渐变更平滑
    const fullDurationMs = 800;
    final durationMs = (fullDurationMs * (delta / 100)).toInt();
    
    // 至少执行一次
    if (durationMs < 50) {
       await _player.setVolume(targetVolume);
      _onFadeComplete(targetVolume);
      return;
    }

    final steps = (durationMs / 50).ceil();
    int currentStep = 0;
    final isFadeOut = targetVolume < startVolume;

    _fadeTimer = Timer.periodic(const Duration(milliseconds: 50), (timer) async {
      currentStep++;
      final progress = currentStep / steps; // 0.0 -> 1.0
      
      double newVolume;
      if (isFadeOut) {
        // 渐出：使用抛物线曲线 (t^2)，使音量在结束时下降更平缓，避免突然切断的感觉
        // t 从 1.0 降到 0.0
        final t = 1.0 - progress;
        // factor 从 1.0 降到 0.0 (非线性)
        final factor = t * t; 
        newVolume = targetVolume + (startVolume - targetVolume) * factor;
      } else {
        // 渐入：保持线性或使用 sqrt
        newVolume = startVolume + (targetVolume - startVolume) * progress;
      }
      
      // 边界检查
      if (newVolume < 0) newVolume = 0;
      if (newVolume > 100) newVolume = 100;
      
      // 检查是否结束
      bool finished = currentStep >= steps;
      // 额外的容错检查
      if (isFadeOut && newVolume <= targetVolume) finished = true;
      if (!isFadeOut && newVolume >= targetVolume) finished = true;

      if (finished) {
        timer.cancel();
        await _player.setVolume(targetVolume);
        _onFadeComplete(targetVolume);
      } else {
        await _player.setVolume(newVolume);
      }
    });
  }

  /// 渐变结束后的清理工作
  Future<void> _onFadeComplete(double targetVolume) async {
    if (targetVolume <= 0) {
      // 只有音量归零时，才真正暂停底层播放器
      await _player.pause();
    } 
    
    // 恢复状态控制权给底层
    _userRequestedPlaying = null;
    
    // 触发一次广播，确保 UI 与最终底层状态同步
    _broadcastState();
  }

  @override
  Future<void> play() async {
    final settings = Hive.box('settings');
    final enableFade = settings.get('enable_fade', defaultValue: true);

    // 1. 立即响应用户意图
    _userRequestedPlaying = true;
    _broadcastState(); // 立即变暂停图标

    if (!enableFade) {
      _userRequestedPlaying = null;
      await _player.setVolume(100);
      return _player.play();
    }

    // 2. 确保底层开始播放 (静音或当前音量)
    if (!_player.state.playing) {
      // 如果之前是暂停的，可能音量还保留在暂停前的位置，或者被设为0了
      // 无论是哪种，我们都从当前音量开始渐变到 100
      await _player.play();
    }

    // 3. 执行渐变到 100
    _setVolumeWithFade(100);
  }

  @override
  Future<void> pause() async {
    final settings = Hive.box('settings');
    final enableFade = settings.get('enable_fade', defaultValue: true);

    // 1. 立即响应用户意图
    _userRequestedPlaying = false;
    _broadcastState(); // 立即变播放图标

    if (!enableFade) {
      _userRequestedPlaying = null;
      return _player.pause();
    }

    // 2. 执行渐变到 0
    // 注意：不要先调用 _player.pause()，否则声音会骤停
    _setVolumeWithFade(0);
  }

  @override
  Future<void> stop() => _player.stop();

  @override
  Future<void> seek(Duration position) => _player.seek(position);

  @override
  Future<void> skipToNext() async {
    if (_playlist.isEmpty) return;

    if (_playMode == PlayMode.shuffle) {
      // 随机播放
      _currentIndex = Random().nextInt(_playlist.length);
      await playVideo(_playlist[_currentIndex]);
    } else if (hasNext) {
      _currentIndex++;
      await playVideo(_playlist[_currentIndex]);
    } else if (_playMode == PlayMode.loop) {
      // 列表循环：回到开头
      _currentIndex = 0;
      await playVideo(_playlist[_currentIndex]);
    }
  }

  @override
  Future<void> skipToPrevious() async {
    if (_playlist.isEmpty) return;

    // 如果播放超过 3 秒，重播当前
    if (_player.state.position.inSeconds > 3) {
      await seek(Duration.zero);
      return;
    } 
    
    if (_playMode == PlayMode.shuffle) {
       _currentIndex = Random().nextInt(_playlist.length);
       await playVideo(_playlist[_currentIndex]);
    } else if (hasPrevious) {
      _currentIndex--;
      await playVideo(_playlist[_currentIndex]);
    } else if (_playMode == PlayMode.loop) {
      // 列表循环：跳到最后一个
      _currentIndex = _playlist.length - 1;
      await playVideo(_playlist[_currentIndex]);
    }
  }
  
  // 辅助方法：清空/设置播放列表等
  
  void setPlaylist(List<VideoModel> videos, {int startIndex = 0}) {
    _playlist.clear();
    _playlist.addAll(videos);
    
    // 只是更新列表，不立即播放（除非 caller 随后调用 playVideo）
    // 但为了 UI 显示，可以更新 index
    if (startIndex >= 0 && startIndex < videos.length) {
      _currentIndex = startIndex;
      // 预先更新 UI 显示
      _updateMediaItem(videos[startIndex]);
    }
    
    _updateQueue();
  }

  void addToPlaylist(VideoModel video) {
    if (!_playlist.any((v) => v.bvid == video.bvid)) {
      _playlist.add(video);
      _updateQueue();
    }
  }

  void removeFromPlaylist(int index) {
    if (index < 0 || index >= _playlist.length) return;
    _playlist.removeAt(index);
    if (index < _currentIndex) {
      _currentIndex--;
    } else if (index == _currentIndex) {
      stop(); // 移除当前播放的，停止
      _currentIndex = -1;
    }
    _updateQueue();
  }
  
  void clearPlaylist() {
    _playlist.clear();
    _currentIndex = -1;
    stop();
    _updateQueue();
  }

  Future<void> skipToIndex(int index) async {
    if (index >= 0 && index < _playlist.length) {
      _currentIndex = index;
      await playVideo(_playlist[index]);
    }
  }

  void _updateQueue() {
    queue.add(_playlist.map((v) => MediaItem(
      id: v.bvid,
      title: v.title,
      artist: v.author,
      artUri: Uri.parse(v.cover),
    )).toList());
  }

  Future<void> dispose() async {
    await _player.dispose();
  }
}
