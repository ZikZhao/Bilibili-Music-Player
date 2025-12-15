import 'dart:async';

import 'package:media_kit/media_kit.dart';

/// Minimal adapter that exposes a small subset of the `just_audio`-like
/// API used by this project while delegating playback to `media_kit`.
///
/// This adapter intentionally implements only the methods/streams used by
/// `audio_handler.dart` and `player_provider.dart` so the rest of the code
///base can remain largely unchanged.
enum ProcessingState { idle, loading, buffering, ready, completed }

class MediaPlayerAdapter {
  final Player _player = Player();

  // Controllers for the streams the app expects
  final StreamController<bool> _playingController =
      StreamController<bool>.broadcast();
  final StreamController<ProcessingState> _processingController =
      StreamController<ProcessingState>.broadcast();
  final StreamController<Duration> _positionController =
      StreamController<Duration>.broadcast();
  final StreamController<Duration> _bufferedPositionController =
      StreamController<Duration>.broadcast();
  final StreamController<Duration?> _durationController =
      StreamController<Duration?>.broadcast();

  MediaPlayerAdapter() {
    // Wire basic streams from media_kit to our controllers.
    // media_kit exposes `streams` with common properties; subscribe if available.
    try {
      _player.streams.playing.listen((playing) {
        _playingController.add(playing);
      });
    } catch (_) {
      // ignore if streams not available in environment
    }

    try {
      _player.streams.position.listen((pos) {
        position = pos;
        _positionController.add(pos);
      });
    } catch (_) {}

    try {
      _player.streams.duration.listen((d) {
        _durationController.add(d);
        if (d != null) {
          // ensure internal position/duration not null
        }
      });
    } catch (_) {}

    // media_kit PlayerStream does not provide a bufferedPosition getter
    // in all platforms. Keep `bufferedPosition` available but leave it
    // updated from other events (or zero) to avoid referencing
    // a non-existent stream.

    // Initialize processing state as idle.
    _processingController.add(ProcessingState.idle);
  }

  // Streams exposed to caller
  Stream<bool> get playingStream => _playingController.stream;
  Stream<ProcessingState> get processingStateStream =>
      _processingController.stream;
  Stream<Duration> get positionStream => _positionController.stream;
  Stream<Duration> get bufferedPositionStream =>
      _bufferedPositionController.stream;
  Stream<Duration?> get durationStream => _durationController.stream;

  // Basic properties (best-effort; some will be updated via streams)
  bool playing = false;
  ProcessingState processingState = ProcessingState.idle;
  Duration position = Duration.zero;
  Duration bufferedPosition = Duration.zero;
  double speed = 1.0;

  /// Open a local file for playback. Returns a best-effort duration (may be null).
  Future<Duration?> setAudioSourceFile(
    String path, {
    Duration? durationTag,
  }) async {
    _processingController.add(ProcessingState.loading);
    processingState = ProcessingState.loading;
    await _player.open(Media(path));
    _processingController.add(ProcessingState.ready);
    processingState = ProcessingState.ready;
    final dur = durationTag;
    _durationController.add(dur);
    // emit initial positions
    _positionController.add(position);
    _bufferedPositionController.add(bufferedPosition);
    return dur;
  }

  /// Open a network URI for playback. Headers are not guaranteed by media_kit's
  /// Media constructor; if headers are required the caller should prefetch to
  /// a local file (the project already has a CacheManager for that).
  Future<Duration?> setAudioSourceUri(
    String uri, {
    Duration? durationTag,
  }) async {
    _processingController.add(ProcessingState.loading);
    processingState = ProcessingState.loading;
    await _player.open(Media(uri));
    _processingController.add(ProcessingState.ready);
    processingState = ProcessingState.ready;
    final dur = durationTag;
    _durationController.add(dur);
    // emit initial positions
    _positionController.add(position);
    _bufferedPositionController.add(bufferedPosition);
    return dur;
  }

  Future<void> play() async {
    await _player.play();
    playing = true;
    _playingController.add(true);
  }

  Future<void> pause() async {
    await _player.pause();
    playing = false;
    _playingController.add(false);
  }

  Future<void> stop() async {
    await _player.stop();
    playing = false;
    _playingController.add(false);
  }

  Future<void> seek(Duration position) async {
    await _player.seek(position);
    this.position = position;
    _positionController.add(position);
  }

  Future<void> setVolume(double volume) async {
    try {
      await _player.setVolume(volume);
    } catch (_) {}
  }

  Future<void> setSpeed(double speed) async {
    try {
      await _player.setRate(speed);
      this.speed = speed;
    } catch (_) {}
  }

  Future<void> dispose() async {
    try {
      await _player.dispose();
    } catch (_) {}
    await _playingController.close();
    await _processingController.close();
    await _positionController.close();
    await _bufferedPositionController.close();
    await _durationController.close();
  }
}
