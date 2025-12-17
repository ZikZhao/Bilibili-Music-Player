class AppConstants {
  static const String bilibiliUserAgent =
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36';
  static const String bilibiliReferer = 'https://www.bilibili.com/';
  
  static const Map<String, String> bilibiliHeaders = {
    'User-Agent': bilibiliUserAgent,
    'Referer': bilibiliReferer,
  };
}
