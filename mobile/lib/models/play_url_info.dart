/// 播放地址信息
class PlayUrlInfo {
  /// 播放 URL（视频流或音频流）
  final String url;

  /// 音频 URL（仅 DASH 视频流时需要）
  final String? audioUrl;

  /// 画质 ID / 音频带宽
  final int quality;

  /// 格式
  final String format;

  /// 文件大小（字节）
  final int size;

  /// 时长（毫秒）
  final int length;

  /// 是否为纯音频流
  final bool isAudioOnly;

  const PlayUrlInfo({
    required this.url,
    this.audioUrl,
    required this.quality,
    required this.format,
    required this.size,
    required this.length,
    this.isAudioOnly = false,
  });

  /// 获取画质名称
  String get qualityName {
    switch (quality) {
      case 16:
        return '360P';
      case 32:
        return '480P';
      case 64:
        return '720P';
      case 80:
        return '1080P';
      case 112:
        return '1080P+';
      case 116:
        return '1080P60';
      case 120:
        return '4K';
      case 125:
        return 'HDR';
      case 126:
        return '杜比视界';
      case 127:
        return '8K';
      default:
        return '未知';
    }
  }
}
