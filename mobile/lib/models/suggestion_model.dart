/// 搜索建议模型
class SuggestionModel {
  /// 建议关键词
  final String value;

  /// 显示名称（可能包含高亮标签，已清理）
  final String name;

  const SuggestionModel({required this.value, required this.name});

  /// 从 API JSON 创建实例
  factory SuggestionModel.fromJson(Map<String, dynamic> json) {
    final rawName = json['name'] as String? ?? '';
    final cleanName = _removeHtmlTags(rawName);

    return SuggestionModel(
      value: json['value'] as String? ?? '',
      name: cleanName.isNotEmpty ? cleanName : json['value'] as String? ?? '',
    );
  }

  /// 去除 HTML 标签
  static String _removeHtmlTags(String text) {
    return text.replaceAll(RegExp(r'<[^>]+>'), '');
  }

  @override
  String toString() => 'SuggestionModel(value: $value)';

  @override
  bool operator ==(Object other) {
    if (identical(this, other)) return true;
    return other is SuggestionModel && other.value == value;
  }

  @override
  int get hashCode => value.hashCode;
}
