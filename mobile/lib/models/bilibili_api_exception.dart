/// B 站 API 异常
class BilibiliApiException implements Exception {
  final String message;

  const BilibiliApiException(this.message);

  @override
  String toString() => 'BilibiliApiException: $message';
}
