import 'dart:async';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:path_provider/path_provider.dart';

/// 下载进度信息
class DownloadProgress {
  final String bvid;
  final int received;
  final int total;
  final bool isComplete;
  final String? error;

  const DownloadProgress({
    required this.bvid,
    required this.received,
    required this.total,
    this.isComplete = false,
    this.error,
  });

  /// 进度百分比 (0.0 - 1.0)
  double get progress {
    if (total <= 0) return 0.0;
    return (received / total).clamp(0.0, 1.0);
  }

  /// 是否有错误
  bool get hasError => error != null;
}

/// 音频缓存管理器
///
/// 单例模式，管理音频文件的本地缓存
class CacheManager {
  CacheManager._();

  static final CacheManager instance = CacheManager._();

  /// Dio 实例
  final Dio _dio = Dio();

  /// 缓存目录
  Directory? _cacheDir;

  /// 是否已初始化
  bool _isInitialized = false;

  /// B 站请求头
  static const Map<String, String> _bilibiliHeaders = {
    'User-Agent':
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
    'Referer': 'https://www.bilibili.com/',
  };

  /// 正在下载的任务（避免重复下载）
  final Map<String, StreamController<DownloadProgress>> _downloadingMap = {};

  /// 下载进度流控制器
  final _progressController = StreamController<DownloadProgress>.broadcast();

  /// 下载进度流（供 UI 监听）
  Stream<DownloadProgress> get progressStream => _progressController.stream;

  /// 初始化缓存管理器
  Future<void> initialize() async {
    if (_isInitialized) return;

    try {
      final appDocDir = await getApplicationDocumentsDirectory();
      _cacheDir = Directory('${appDocDir.path}/audio_cache');

      // 确保目录存在
      if (!await _cacheDir!.exists()) {
        await _cacheDir!.create(recursive: true);
      }

      _isInitialized = true;
      debugPrint('[CacheManager] 初始化成功: ${_cacheDir!.path}');
    } catch (e) {
      debugPrint('[CacheManager] 初始化失败: $e');
    }
  }

  /// 获取缓存文件路径
  ///
  /// 如果文件存在则返回路径，否则返回 null
  Future<String?> getAudioPath(String bvid) async {
    if (!_isInitialized || _cacheDir == null) return null;

    final sanitizedBvid = _sanitizeBvid(bvid);
    final file = File('${_cacheDir!.path}/$sanitizedBvid.m4s');

    if (await file.exists()) {
      debugPrint('[CacheManager] 缓存命中: $bvid');
      return file.path;
    }

    return null;
  }

  /// 检查是否已缓存
  Future<bool> isCached(String bvid) async {
    final path = await getAudioPath(bvid);
    return path != null;
  }

  /// 检查是否正在下载
  bool isDownloading(String bvid) {
    final sanitizedBvid = _sanitizeBvid(bvid);
    return _downloadingMap.containsKey(sanitizedBvid);
  }

  /// 下载音频文件
  ///
  /// [url] 音频流地址
  /// [bvid] 视频 BV 号（用作文件名）
  /// [onProgress] 下载进度回调
  ///
  /// 返回下载完成后的文件路径，如果下载失败返回 null
  Future<String?> downloadAudio(
    String url,
    String bvid, {
    void Function(int received, int total)? onProgress,
  }) async {
    if (!_isInitialized || _cacheDir == null) {
      debugPrint('[CacheManager] 未初始化，无法下载');
      return null;
    }

    final sanitizedBvid = _sanitizeBvid(bvid);

    // 避免重复下载
    if (_downloadingMap.containsKey(sanitizedBvid)) {
      debugPrint('[CacheManager] 正在下载中，跳过: $bvid');
      return null;
    }

    // 检查是否已缓存
    final existingPath = await getAudioPath(bvid);
    if (existingPath != null) {
      debugPrint('[CacheManager] 已缓存，跳过下载: $bvid');
      // 通知缓存命中（进度 100%）
      _progressController.add(
        DownloadProgress(bvid: bvid, received: 1, total: 1, isComplete: true),
      );
      return existingPath;
    }

    // 创建下载流控制器
    final controller = StreamController<DownloadProgress>.broadcast();
    _downloadingMap[sanitizedBvid] = controller;

    final filePath = '${_cacheDir!.path}/$sanitizedBvid.m4s';
    final tempPath = '$filePath.tmp';

    try {
      debugPrint('[CacheManager] 开始下载: $bvid');

      int lastLoggedPercent = -1;
      await _dio.download(
        url,
        tempPath,
        options: Options(headers: _bilibiliHeaders),
        onReceiveProgress: (received, total) {
          onProgress?.call(received, total);

          // 广播下载进度
          final progress = DownloadProgress(
            bvid: bvid,
            received: received,
            total: total,
          );
          controller.add(progress);
          _progressController.add(progress);

          // 每 10% 打印一次日志
          if (total > 0) {
            final percent = (received / total * 100).toInt();
            if (percent ~/ 10 > lastLoggedPercent) {
              lastLoggedPercent = percent ~/ 10;
              debugPrint('[CacheManager] 下载进度: $percent%');
            }
          }
        },
      );

      // 下载完成，重命名文件（原子操作，防止读取不完整文件）
      final tempFile = File(tempPath);
      if (await tempFile.exists()) {
        await tempFile.rename(filePath);
        debugPrint('[CacheManager] 下载完成: $bvid');

        // 广播完成状态
        final completeProgress = DownloadProgress(
          bvid: bvid,
          received: 1,
          total: 1,
          isComplete: true,
        );
        controller.add(completeProgress);
        _progressController.add(completeProgress);

        return filePath;
      }
    } catch (e) {
      debugPrint('[CacheManager] 下载失败: $e');

      // 广播错误状态
      final errorProgress = DownloadProgress(
        bvid: bvid,
        received: 0,
        total: 0,
        error: e.toString(),
      );
      controller.add(errorProgress);
      _progressController.add(errorProgress);

      // 清理临时文件
      final tempFile = File(tempPath);
      if (await tempFile.exists()) {
        await tempFile.delete();
      }
    } finally {
      await controller.close();
      _downloadingMap.remove(sanitizedBvid);
    }

    return null;
  }

  /// 后台下载（不阻塞）
  ///
  /// 用于收藏时自动缓存
  void downloadInBackground(String url, String bvid) {
    // Fire and forget
    downloadAudio(url, bvid)
        .then((path) {
          if (path != null) {
            debugPrint('[CacheManager] 后台下载完成: $bvid');
          }
        })
        .catchError((e) {
          debugPrint('[CacheManager] 后台下载失败: $e');
        });
  }

  /// 删除缓存文件
  Future<bool> deleteCache(String bvid) async {
    if (!_isInitialized || _cacheDir == null) return false;

    final sanitizedBvid = _sanitizeBvid(bvid);
    final file = File('${_cacheDir!.path}/$sanitizedBvid.m4s');

    if (await file.exists()) {
      await file.delete();
      debugPrint('[CacheManager] 删除缓存: $bvid');
      return true;
    }

    return false;
  }

  /// 清空所有缓存
  Future<void> clearAll() async {
    if (!_isInitialized || _cacheDir == null) return;

    try {
      if (await _cacheDir!.exists()) {
        await _cacheDir!.delete(recursive: true);
        await _cacheDir!.create(recursive: true);
        debugPrint('[CacheManager] 已清空所有缓存');
      }
    } catch (e) {
      debugPrint('[CacheManager] 清空缓存失败: $e');
    }
  }

  /// 获取缓存大小（字节）
  Future<int> getCacheSize() async {
    if (!_isInitialized || _cacheDir == null) return 0;

    int totalSize = 0;
    try {
      if (await _cacheDir!.exists()) {
        await for (final entity in _cacheDir!.list(recursive: true)) {
          if (entity is File) {
            totalSize += await entity.length();
          }
        }
      }
    } catch (e) {
      debugPrint('[CacheManager] 获取缓存大小失败: $e');
    }

    return totalSize;
  }

  /// 获取格式化的缓存大小
  Future<String> getFormattedCacheSize() async {
    final size = await getCacheSize();
    if (size < 1024) {
      return '$size B';
    } else if (size < 1024 * 1024) {
      return '${(size / 1024).toStringAsFixed(1)} KB';
    } else if (size < 1024 * 1024 * 1024) {
      return '${(size / (1024 * 1024)).toStringAsFixed(1)} MB';
    } else {
      return '${(size / (1024 * 1024 * 1024)).toStringAsFixed(2)} GB';
    }
  }

  /// 清理 BV 号（移除非法字符）
  String _sanitizeBvid(String bvid) {
    // BV 号只包含字母和数字，但为了安全还是过滤一下
    return bvid.replaceAll(RegExp(r'[^a-zA-Z0-9]'), '_');
  }
}
