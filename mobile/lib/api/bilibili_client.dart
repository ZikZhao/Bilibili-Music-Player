import 'package:cookie_jar/cookie_jar.dart';
import 'package:dio/dio.dart';
import 'package:dio_cookie_manager/dio_cookie_manager.dart';
import 'package:flutter/foundation.dart';

import '../models/suggestion_model.dart';
import '../models/video_model.dart';
import 'interceptors/bilibili_header_interceptor.dart';
import 'wbi_signer.dart';

/// B 站 API 客户端
///
/// 封装所有 B 站 API 调用，包括搜索、视频信息等
class BilibiliClient {
  static const String _baseUrl = 'https://api.bilibili.com';
  static const String _suggestUrl = 'https://s.search.bilibili.com';

  final Dio _dio;
  final WbiSigner _wbiSigner;

  bool _isInitialized = false;

  /// 是否已初始化（获取了 Cookie 和 WBI 密钥）
  bool get isInitialized => _isInitialized;

  final CookieJar _cookieJar;

  BilibiliClient()
    : _dio = Dio(),
      _wbiSigner = WbiSigner(),
      _cookieJar = CookieJar() {
    // Cookie 管理器 - 关键！模拟浏览器的 Cookie 行为
    _dio.interceptors.add(CookieManager(_cookieJar));
    _dio.interceptors.add(BilibiliHeaderInterceptor());
    _dio.options.connectTimeout = const Duration(seconds: 15);
    _dio.options.receiveTimeout = const Duration(seconds: 15);
  }

  /// 初始化客户端
  ///
  /// 1. 访问 B 站主页获取必要的 Cookie (buvid3)
  /// 2. 获取 WBI 签名密钥
  Future<void> initialize() async {
    if (_isInitialized) return;

    try {
      debugPrint('[BilibiliClient] 开始初始化...');

      // 1. 获取 Cookie - 先访问主页
      debugPrint('[BilibiliClient] 访问 bilibili.com 获取 Cookie...');
      await _dio.get('https://www.bilibili.com/');

      // 打印获取的 Cookie
      final cookies = await _cookieJar.loadForRequest(
        Uri.parse('https://www.bilibili.com'),
      );
      debugPrint('[BilibiliClient] 获取到 Cookie: $cookies');

      // 2. 获取 WBI 密钥
      await _fetchWbiKeys();

      _isInitialized = true;
      debugPrint('[BilibiliClient] 初始化成功!');
    } catch (e, stack) {
      debugPrint('[BilibiliClient] 初始化失败: $e');
      debugPrint('[BilibiliClient] Stack: $stack');
      throw BilibiliApiException('初始化失败: $e');
    }
  }

  /// 获取 WBI 签名密钥
  Future<void> _fetchWbiKeys() async {
    debugPrint('[BilibiliClient] 获取 WBI 密钥...');
    final response = await _dio.get('$_baseUrl/x/web-interface/nav');
    final data = response.data;

    // 注意：code=-101 表示未登录，但仍然会返回 wbi_img
    // 只有 code 不是 0 且不是 -101 时才报错
    final code = data['code'] as int?;
    debugPrint('[BilibiliClient] nav API code=$code');
    if (code != 0 && code != -101) {
      throw BilibiliApiException('获取 WBI 密钥失败: ${data['message']}');
    }

    final wbiImg = data['data']?['wbi_img'];
    if (wbiImg == null) {
      throw BilibiliApiException('获取 WBI 密钥失败: wbi_img 为空');
    }

    final imgUrl = wbiImg['img_url'] as String?;
    final subUrl = wbiImg['sub_url'] as String?;

    if (imgUrl == null || subUrl == null) {
      throw BilibiliApiException('获取 WBI 密钥失败: img_url 或 sub_url 为空');
    }

    // 从 URL 提取 key：取最后一个 / 后的文件名，去掉扩展名
    final imgKey = imgUrl.split('/').last.split('.').first;
    final subKey = subUrl.split('/').last.split('.').first;

    debugPrint('[BilibiliClient] WBI imgKey=$imgKey');
    debugPrint('[BilibiliClient] WBI subKey=$subKey');

    _wbiSigner.setKeys(imgKey, subKey);
  }

  /// 获取搜索建议
  ///
  /// [keyword] 搜索关键词
  /// 返回建议列表（最多 10 个）
  Future<List<SuggestionModel>> fetchSuggestions(String keyword) async {
    if (keyword.trim().isEmpty) {
      return [];
    }

    try {
      final response = await _dio.get(
        '$_suggestUrl/main/suggest',
        queryParameters: {
          'term': keyword,
          'main_ver': 'v1',
          'func': 'suggest',
          'suggest_type': 'accurate',
          'sub_type': 'tag',
          'tag_num': 10,
        },
      );

      final data = response.data;
      debugPrint('[BilibiliClient] 搜索建议响应 code: ${data['code']}');

      if (data['code'] != 0) {
        debugPrint('[BilibiliClient] 搜索建议失败: ${data['message']}');
        return [];
      }

      final result = data['result'];
      if (result == null) return [];

      final tag = result['tag'] as List<dynamic>?;
      if (tag == null) {
        debugPrint('[BilibiliClient] 搜索建议: tag 为空');
        return [];
      }

      debugPrint('[BilibiliClient] 获取到 ${tag.length} 条搜索建议');
      return tag
          .map((item) => SuggestionModel.fromJson(item as Map<String, dynamic>))
          .toList();
    } catch (e, stack) {
      // 建议接口失败不抛异常，返回空列表
      debugPrint('[BilibiliClient] 搜索建议异常: $e');
      debugPrint('[BilibiliClient] Stack: $stack');
      return [];
    }
  }

  /// 搜索视频
  ///
  /// [keyword] 搜索关键词
  /// [page] 页码，默认 1
  /// [pageSize] 每页数量，默认 20
  Future<SearchResult> searchVideos(
    String keyword, {
    int page = 1,
    int pageSize = 20,
  }) async {
    await _ensureInitialized();

    final params = {
      'search_type': 'video',
      'keyword': keyword,
      'page': page,
      'page_size': pageSize,
    };

    // WBI 签名
    final signedParams = _wbiSigner.sign(params);
    debugPrint('[BilibiliClient] 搜索参数: $signedParams');

    try {
      final response = await _dio.get(
        '$_baseUrl/x/web-interface/wbi/search/type',
        queryParameters: signedParams,
      );

      final data = response.data;
      debugPrint('[BilibiliClient] 搜索响应 code: ${data['code']}');

      if (data['code'] != 0) {
        throw BilibiliApiException('搜索失败: ${data['message']}');
      }

      final resultData = data['data'];
      final resultList = resultData['result'] as List<dynamic>?;

      if (resultList == null || resultList.isEmpty) {
        return SearchResult(
          videos: [],
          page: page,
          pageSize: pageSize,
          numResults: 0,
          numPages: 0,
        );
      }

      final videos = resultList
          .map(
            (item) => VideoModel.fromSearchJson(item as Map<String, dynamic>),
          )
          .toList();

      return SearchResult(
        videos: videos,
        page: resultData['page'] as int? ?? page,
        pageSize: resultData['pagesize'] as int? ?? pageSize,
        numResults: resultData['numResults'] as int? ?? videos.length,
        numPages: resultData['numPages'] as int? ?? 1,
      );
    } on DioException catch (e) {
      debugPrint('[BilibiliClient] 搜索 DioException: ${e.type}');
      debugPrint('[BilibiliClient] HTTP Status: ${e.response?.statusCode}');
      debugPrint('[BilibiliClient] Response: ${e.response?.data}');
      if (e.response?.statusCode == 412) {
        throw BilibiliApiException('请求被拦截(412)，可能需要验证码或 Cookie 过期。请重启应用重试。');
      }
      throw BilibiliApiException('网络请求失败: ${e.message}');
    }
  }

  /// 获取视频详情
  ///
  /// [bvid] 视频 BV 号
  /// 返回视频详情信息（包含 cid）
  Future<VideoDetailInfo> fetchVideoInfo(String bvid) async {
    await _ensureInitialized();

    debugPrint('[BilibiliClient] 获取视频详情: $bvid');

    try {
      final response = await _dio.get(
        '$_baseUrl/x/web-interface/view',
        queryParameters: {'bvid': bvid},
      );

      final data = response.data;
      debugPrint('[BilibiliClient] 视频详情响应 code: ${data['code']}');

      if (data['code'] != 0) {
        throw BilibiliApiException('获取视频详情失败: ${data['message']}');
      }

      return VideoDetailInfo.fromJson(data['data'] as Map<String, dynamic>);
    } on DioException catch (e) {
      debugPrint('[BilibiliClient] 视频详情 DioException: ${e.type}');
      throw BilibiliApiException('获取视频详情失败: ${e.message}');
    }
  }

  /// 获取视频播放地址
  ///
  /// [bvid] 视频 BV 号
  /// [cid] 分 P 的 cid
  /// 返回播放 URL（MP4 直链）
  Future<PlayUrlInfo> fetchPlayUrl(String bvid, int cid) async {
    await _ensureInitialized();

    debugPrint('[BilibiliClient] 获取播放地址: bvid=$bvid, cid=$cid');

    // 请求 MP4 直链: fnval=1 表示 MP4 格式
    final params = {
      'bvid': bvid,
      'cid': cid,
      'qn': 64, // 720P
      'fnval': 1, // MP4 格式 (非 DASH)
      'fnver': 0,
      'fourk': 1,
    };

    // WBI 签名
    final signedParams = _wbiSigner.sign(params);
    debugPrint('[BilibiliClient] 播放地址参数: $signedParams');

    try {
      final response = await _dio.get(
        '$_baseUrl/x/player/wbi/playurl',
        queryParameters: signedParams,
      );

      final data = response.data;
      debugPrint('[BilibiliClient] 播放地址响应 code: ${data['code']}');

      if (data['code'] != 0) {
        throw BilibiliApiException('获取播放地址失败: ${data['message']}');
      }

      final resultData = data['data'] as Map<String, dynamic>;

      // 优先取 durl (MP4 直链)
      final durl = resultData['durl'] as List<dynamic>?;
      if (durl == null || durl.isEmpty) {
        throw BilibiliApiException('获取播放地址失败: 无可用的 MP4 直链');
      }

      final firstUrl = durl[0] as Map<String, dynamic>;
      final url = firstUrl['url'] as String;
      final size = firstUrl['size'] as int? ?? 0;
      final length = firstUrl['length'] as int? ?? 0;

      debugPrint('[BilibiliClient] 播放地址获取成功: ${url.substring(0, 80)}...');

      return PlayUrlInfo(
        url: url,
        quality: resultData['quality'] as int? ?? 64,
        format: resultData['format'] as String? ?? 'mp4',
        size: size,
        length: length,
      );
    } on DioException catch (e) {
      debugPrint('[BilibiliClient] 播放地址 DioException: ${e.type}');
      debugPrint('[BilibiliClient] HTTP Status: ${e.response?.statusCode}');
      if (e.response?.statusCode == 412) {
        throw BilibiliApiException('请求被拦截(412)，请重启应用重试。');
      }
      throw BilibiliApiException('获取播放地址失败: ${e.message}');
    }
  }

  /// 确保已初始化
  Future<void> _ensureInitialized() async {
    if (!_isInitialized) {
      await initialize();
    }
  }
}

/// 搜索结果
class SearchResult {
  final List<VideoModel> videos;
  final int page;
  final int pageSize;
  final int numResults;
  final int numPages;

  const SearchResult({
    required this.videos,
    required this.page,
    required this.pageSize,
    required this.numResults,
    required this.numPages,
  });

  bool get hasMore => page < numPages;
}

/// B 站 API 异常
class BilibiliApiException implements Exception {
  final String message;

  const BilibiliApiException(this.message);

  @override
  String toString() => 'BilibiliApiException: $message';
}

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

/// 播放地址信息
class PlayUrlInfo {
  /// 播放 URL
  final String url;

  /// 画质 ID
  final int quality;

  /// 格式
  final String format;

  /// 文件大小（字节）
  final int size;

  /// 时长（毫秒）
  final int length;

  const PlayUrlInfo({
    required this.url,
    required this.quality,
    required this.format,
    required this.size,
    required this.length,
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
