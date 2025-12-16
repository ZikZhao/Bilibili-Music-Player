import 'package:hive/hive.dart';

part 'video_model.g.dart';

/// 视频搜索结果模型
///
/// 从 B 站搜索 API 返回的视频信息
@HiveType(typeId: 0)
class VideoModel {
  /// 视频 BV 号
  @HiveField(0)
  final String bvid;

  /// 视频 AV 号
  @HiveField(1)
  final int aid;

  /// 视频标题（已去除 HTML 标签）
  @HiveField(2)
  final String title;

  /// UP 主名称
  @HiveField(3)
  final String author;

  /// UP 主 MID
  @HiveField(4)
  final int mid;

  /// 视频封面图 URL
  @HiveField(5)
  final String cover;

  /// 视频时长字符串（如 "04:30"）
  @HiveField(6)
  final String duration;

  /// 播放量
  @HiveField(7)
  final int playCount;

  /// 弹幕数
  @HiveField(8)
  final int danmakuCount;

  /// 收藏数
  @HiveField(9)
  final int favoriteCount;

  /// 发布时间戳
  @HiveField(10)
  final int pubdate;

  /// 添加到收藏的时间（用于排序）
  @HiveField(11)
  final DateTime? addedAt;

  const VideoModel({
    required this.bvid,
    required this.aid,
    required this.title,
    required this.author,
    required this.mid,
    required this.cover,
    required this.duration,
    required this.playCount,
    this.danmakuCount = 0,
    this.favoriteCount = 0,
    this.pubdate = 0,
    this.addedAt,
  });

  /// 从搜索 API JSON 创建实例
  factory VideoModel.fromSearchJson(Map<String, dynamic> json) {
    // 去除标题中的 HTML 高亮标签
    final rawTitle = json['title'] as String? ?? '';
    final cleanTitle = _removeHtmlTags(rawTitle);

    // 封面 URL 处理：添加 https 协议
    var cover = json['pic'] as String? ?? '';
    if (cover.startsWith('//')) {
      cover = 'https:$cover';
    }

    return VideoModel(
      bvid: json['bvid'] as String? ?? '',
      aid: json['aid'] as int? ?? 0,
      title: cleanTitle,
      author: json['author'] as String? ?? '',
      mid: json['mid'] as int? ?? 0,
      cover: cover,
      duration: json['duration'] as String? ?? '00:00',
      playCount: _parsePlayCount(json['play']),
      danmakuCount: json['video_review'] as int? ?? 0,
      favoriteCount: json['favorites'] as int? ?? 0,
      pubdate: json['pubdate'] as int? ?? 0,
    );
  }

  /// 去除 HTML 标签
  static String _removeHtmlTags(String text) {
    return text.replaceAll(RegExp(r'<[^>]+>'), '');
  }

  /// 解析播放量（可能是 int 或 String）
  static int _parsePlayCount(dynamic play) {
    if (play == null) return 0;
    if (play is int) return play;
    if (play is String) {
      return int.tryParse(play) ?? 0;
    }
    return 0;
  }

  /// 格式化播放量（如 "10.5万"）
  String get formattedPlayCount {
    if (playCount >= 100000000) {
      return '${(playCount / 100000000).toStringAsFixed(1)}亿';
    } else if (playCount >= 10000) {
      return '${(playCount / 10000).toStringAsFixed(1)}万';
    }
    return playCount.toString();
  }

  /// 格式化时长（确保分钟和秒数都是两位数）
  ///
  /// 例如：
  /// - 7:07 (7分7秒) -> "07:07"
  /// - 1:07:07 (1小时7分7秒) -> "1:07:07"
  String get formattedDuration {
    final totalSeconds = durationSeconds;
    final hours = totalSeconds ~/ 3600;
    final minutes = (totalSeconds % 3600) ~/ 60;
    final seconds = totalSeconds % 60;

    if (hours > 0) {
      return '$hours:${minutes.toString().padLeft(2, '0')}:${seconds.toString().padLeft(2, '0')}';
    }
    return '${minutes.toString().padLeft(2, '0')}:${seconds.toString().padLeft(2, '0')}';
  }

  /// 时长秒数（从 "MM:SS" 或 "HH:MM:SS" 解析）
  int get durationSeconds {
    final parts = duration.split(':');
    if (parts.length == 2) {
      final minutes = int.tryParse(parts[0]) ?? 0;
      final seconds = int.tryParse(parts[1]) ?? 0;
      return minutes * 60 + seconds;
    } else if (parts.length == 3) {
      final hours = int.tryParse(parts[0]) ?? 0;
      final minutes = int.tryParse(parts[1]) ?? 0;
      final seconds = int.tryParse(parts[2]) ?? 0;
      return hours * 3600 + minutes * 60 + seconds;
    }
    return 0;
  }

  /// 获取有效的添加时间（兼容旧数据）
  DateTime get effectiveAddedAt => addedAt ?? DateTime(2000);

  /// 复制并修改部分字段
  VideoModel copyWith({
    String? bvid,
    int? aid,
    String? title,
    String? author,
    int? mid,
    String? cover,
    String? duration,
    int? playCount,
    int? danmakuCount,
    int? favoriteCount,
    int? pubdate,
    DateTime? addedAt,
  }) {
    return VideoModel(
      bvid: bvid ?? this.bvid,
      aid: aid ?? this.aid,
      title: title ?? this.title,
      author: author ?? this.author,
      mid: mid ?? this.mid,
      cover: cover ?? this.cover,
      duration: duration ?? this.duration,
      playCount: playCount ?? this.playCount,
      danmakuCount: danmakuCount ?? this.danmakuCount,
      favoriteCount: favoriteCount ?? this.favoriteCount,
      pubdate: pubdate ?? this.pubdate,
      addedAt: addedAt ?? this.addedAt,
    );
  }

  @override
  String toString() {
    return 'VideoModel(bvid: $bvid, title: $title, author: $author)';
  }

  @override
  bool operator ==(Object other) {
    if (identical(this, other)) return true;
    return other is VideoModel && other.bvid == bvid;
  }

  @override
  int get hashCode => bvid.hashCode;
}
