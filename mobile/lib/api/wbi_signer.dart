import 'dart:convert';

import 'package:crypto/crypto.dart';

/// WBI 签名工具类
///
/// 用于对 B 站 API 请求参数进行签名
/// 签名算法参考：https://github.com/SocialSisterYi/bilibili-API-collect
class WbiSigner {
  /// WBI 签名混淆表
  static const List<int> _mixinKeyEncTab = [
    46,
    47,
    18,
    2,
    53,
    8,
    23,
    32,
    15,
    50,
    10,
    31,
    58,
    3,
    45,
    35,
    27,
    43,
    5,
    49,
    33,
    9,
    42,
    19,
    29,
    28,
    14,
    39,
    12,
    38,
    41,
    13,
    37,
    48,
    7,
    16,
    24,
    55,
    40,
    61,
    26,
    17,
    0,
    1,
    60,
    51,
    30,
    4,
    22,
    25,
    54,
    21,
    56,
    59,
    6,
    63,
    57,
    62,
    11,
    36,
    20,
    34,
    44,
    52,
  ];

  String? _imgKey;
  String? _subKey;

  /// 是否已初始化
  bool get isInitialized => _imgKey != null && _subKey != null;

  /// 设置 WBI 密钥
  void setKeys(String imgKey, String subKey) {
    _imgKey = imgKey;
    _subKey = subKey;
  }

  /// 从 imgKey + subKey 生成混淆密钥
  String _getMixinKey(String orig) {
    final buffer = StringBuffer();
    for (var i = 0; i < 32; i++) {
      buffer.write(orig[_mixinKeyEncTab[i]]);
    }
    return buffer.toString();
  }

  /// 对请求参数进行 WBI 签名
  ///
  /// [params] 原始请求参数
  /// 返回签名后的参数（包含 wts 和 w_rid）
  Map<String, dynamic> sign(Map<String, dynamic> params) {
    if (!isInitialized) {
      throw StateError('WBI keys not initialized. Call setKeys() first.');
    }

    final mixinKey = _getMixinKey(_imgKey! + _subKey!);
    final currTime = DateTime.now().millisecondsSinceEpoch ~/ 1000;

    // 复制参数并添加时间戳
    final signedParams = Map<String, dynamic>.from(params);
    signedParams['wts'] = currTime;

    // 按 key 排序
    final sortedKeys = signedParams.keys.toList()..sort();
    final sortedParams = <String, dynamic>{};
    for (final key in sortedKeys) {
      sortedParams[key] = signedParams[key];
    }

    // 过滤 value 中的特殊字符 "!'()*"
    final filteredParams = sortedParams.map((key, value) {
      final filtered = value.toString().replaceAll(RegExp(r"[!'()*]"), '');
      return MapEntry(key, filtered);
    });

    // 构建查询字符串
    final queryParts = filteredParams.entries
        .map(
          (e) =>
              '${Uri.encodeComponent(e.key)}=${Uri.encodeComponent(e.value)}',
        )
        .toList();
    final query = queryParts.join('&');

    // 计算 w_rid
    final wbiSign = md5.convert(utf8.encode(query + mixinKey)).toString();

    return {...filteredParams, 'w_rid': wbiSign};
  }
}
