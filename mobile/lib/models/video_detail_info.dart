/// 视频详情信息
class VideoDetailInfo {
  /// 视频 BV 号
  final String bvid;

  /// 视频 AV 号
  final int aid;

  /// 第一个分 P 的 cid
  final int cid;

  /// 视频标题
  final String title;

  /// 视频简介
  final String desc;

  /// 视频封面
  final String cover;

  /// UP 主信息
  final OwnerInfo owner;

  /// 视频数据统计
  final VideoStat stat;

  /// 发布时间戳
  final int pubdate;

  /// 视频时长（秒）
  final int duration;

  /// 分 P 数量
  final int videos;

  const VideoDetailInfo({
    required this.bvid,
    required this.aid,
    required this.cid,
    required this.title,
    required this.desc,
    required this.cover,
    required this.owner,
    required this.stat,
    required this.pubdate,
    required this.duration,
    required this.videos,
  });

  factory VideoDetailInfo.fromJson(Map<String, dynamic> json) {
    var cover = json['pic'] as String? ?? '';
    if (cover.startsWith('//')) {
      cover = 'https:$cover';
    }

    return VideoDetailInfo(
      bvid: json['bvid'] as String? ?? '',
      aid: json['aid'] as int? ?? 0,
      cid: json['cid'] as int? ?? 0,
      title: json['title'] as String? ?? '',
      desc: json['desc'] as String? ?? '',
      cover: cover,
      owner: OwnerInfo.fromJson(json['owner'] as Map<String, dynamic>? ?? {}),
      stat: VideoStat.fromJson(json['stat'] as Map<String, dynamic>? ?? {}),
      pubdate: json['pubdate'] as int? ?? 0,
      duration: json['duration'] as int? ?? 0,
      videos: json['videos'] as int? ?? 1,
    );
  }

  /// 格式化发布时间
  String get formattedPubdate {
    final date = DateTime.fromMillisecondsSinceEpoch(pubdate * 1000);
    return '${date.year}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';
  }

  /// 格式化时长
  String get formattedDuration {
    final hours = duration ~/ 3600;
    final minutes = (duration % 3600) ~/ 60;
    final seconds = duration % 60;

    if (hours > 0) {
      return '$hours:${minutes.toString().padLeft(2, '0')}:${seconds.toString().padLeft(2, '0')}';
    }
    return '${minutes.toString().padLeft(2, '0')}:${seconds.toString().padLeft(2, '0')}';
  }
}

/// UP 主信息
class OwnerInfo {
  final int mid;
  final String name;
  final String face;

  const OwnerInfo({required this.mid, required this.name, required this.face});

  factory OwnerInfo.fromJson(Map<String, dynamic> json) {
    var face = json['face'] as String? ?? '';
    if (face.startsWith('//')) {
      face = 'https:$face';
    }

    return OwnerInfo(
      mid: json['mid'] as int? ?? 0,
      name: json['name'] as String? ?? '',
      face: face,
    );
  }
}

/// 视频统计数据
class VideoStat {
  final int view;
  final int danmaku;
  final int reply;
  final int favorite;
  final int coin;
  final int share;
  final int like;

  const VideoStat({
    required this.view,
    required this.danmaku,
    required this.reply,
    required this.favorite,
    required this.coin,
    required this.share,
    required this.like,
  });

  factory VideoStat.fromJson(Map<String, dynamic> json) {
    return VideoStat(
      view: json['view'] as int? ?? 0,
      danmaku: json['danmaku'] as int? ?? 0,
      reply: json['reply'] as int? ?? 0,
      favorite: json['favorite'] as int? ?? 0,
      coin: json['coin'] as int? ?? 0,
      share: json['share'] as int? ?? 0,
      like: json['like'] as int? ?? 0,
    );
  }

  /// 格式化播放量
  String get formattedView {
    if (view >= 100000000) {
      return '${(view / 100000000).toStringAsFixed(1)}亿';
    } else if (view >= 10000) {
      return '${(view / 10000).toStringAsFixed(1)}万';
    }
    return view.toString();
  }

  /// 格式化弹幕数
  String get formattedDanmaku {
    if (danmaku >= 10000) {
      return '${(danmaku / 10000).toStringAsFixed(1)}万';
    }
    return danmaku.toString();
  }
}
