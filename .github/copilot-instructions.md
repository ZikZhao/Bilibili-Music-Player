# BilibiliMusicPlayer - Copilot Instructions

## 🎯 Role Definition

You are a **Senior Flutter Developer** specializing in:

- **Clean Architecture** with clear separation of concerns (API / Models / Pages / Widgets / Utils)
- **High-Performance UI** optimized for smooth 60fps rendering
- **Cross-Platform Development** targeting Android and Windows

---

## 📦 Technology Stack (Locked)

| Category             | Package                          | Purpose                           |
| -------------------- | -------------------------------- | --------------------------------- |
| **Network**          | `dio`                            | HTTP client with interceptors     |
| **Audio Playback**   | `just_audio` + `audio_service`   | Background audio & media controls |
| **State Management** | `provider`                       | Reactive state across widgets     |
| **Image Caching**    | `cached_network_image`           | Efficient network image loading   |
| **Crypto**           | `crypto` (from `package:crypto`) | MD5 hashing for WBI signature     |
| **Encoding**         | `dart:convert`                   | URL encoding & JSON parsing       |

---

## 🔐 Bilibili API Migration Rules

All network logic **MUST** be ported from `test_player.py`. Follow these critical rules:

### 1. WBI Signature Algorithm

Implement the exact WBI signing logic from Python:

```dart
/// WBI 签名混淆表 - MUST match Python's MIXIN_KEY_ENC_TAB exactly
const List<int> mixinKeyEncTab = [
  46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49,
  33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55, 40,
  61, 26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11,
  36, 20, 34, 44, 52,
];

/// Generate mixin key from imgKey + subKey
String getMixinKey(String orig) {
  return mixinKeyEncTab.map((i) => orig[i]).take(32).join();
}

/// Sign parameters with WBI
Map<String, dynamic> encWbi(
  Map<String, dynamic> params,
  String imgKey,
  String subKey,
) {
  final mixinKey = getMixinKey(imgKey + subKey);
  final currTime = DateTime.now().millisecondsSinceEpoch ~/ 1000;

  params['wts'] = currTime;

  // Sort by key
  final sortedParams = Map.fromEntries(
    params.entries.toList()..sort((a, b) => a.key.compareTo(b.key)),
  );

  // Filter illegal characters from values
  final filteredParams = sortedParams.map((key, value) {
    final filtered = value.toString().replaceAll(RegExp(r"[!'()*]"), '');
    return MapEntry(key, filtered);
  });

  // Build query string
  final query = filteredParams.entries
      .map((e) => '${Uri.encodeComponent(e.key)}=${Uri.encodeComponent(e.value)}')
      .join('&');

  // Calculate w_rid
  final wbiSign = md5.convert(utf8.encode(query + mixinKey)).toString();

  return {...filteredParams, 'w_rid': wbiSign};
}
```

### 2. Mandatory HTTP Headers

**NEVER** send requests without these headers. Create a Dio interceptor:

```dart
class BilibiliHeaderInterceptor extends Interceptor {
  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    options.headers.addAll({
      'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
      'Referer': 'https://www.bilibili.com/',
      'Accept': 'application/json, text/plain, */*',
      'Accept-Language': 'zh-CN,zh;q=0.9,en;q=0.8',
      'Origin': 'https://www.bilibili.com',
    });
    handler.next(options);
  }
}
```

### 3. API Endpoints Reference

| Function        | Endpoint                               | Requires WBI |
| --------------- | -------------------------------------- | ------------ |
| Get WBI Keys    | `GET /x/web-interface/nav`             | ❌           |
| Search Video    | `GET /x/web-interface/wbi/search/type` | ✅           |
| Video Info      | `GET /x/web-interface/view`            | ❌           |
| Play URL (DASH) | `GET /x/player/wbi/playurl`            | ✅           |

Base URL: `https://api.bilibili.com`

### 4. Audio Quality Codes

```dart
enum AudioQuality {
  low(30216, '64K'),
  medium(30232, '132K'),
  high(30280, '192K'),
  dolby(30250, '杜比全景声'),
  hiRes(30251, 'Hi-Res无损');

  const AudioQuality(this.id, this.label);
  final int id;
  final String label;
}
```

### 5. Session Initialization Flow

1. Visit `https://www.bilibili.com/` to obtain `buvid3` cookie
2. Fetch WBI keys from `/x/web-interface/nav`
3. Store `img_key` and `sub_key` for signing subsequent requests

---

## 📝 Code Style Rules (Enforced)

Based on `Dart Code Style.md`, these rules are **MANDATORY**:

### Naming Conventions

| Type           | Style                | Example                                |
| -------------- | -------------------- | -------------------------------------- |
| Class          | PascalCase           | `VideoPlayer`, `WbiSigner`             |
| Enum Type      | PascalCase           | `PlayState`, `AudioQuality`            |
| Enum Value     | lowerCamelCase       | `playing`, `paused`                    |
| Variable       | lowerCamelCase       | `videoUrl`, `currentIndex`             |
| Private Member | `_` + lowerCamelCase | `_isLoading`, `_player`                |
| Constant       | lowerCamelCase       | `defaultTimeout`, `maxRetries`         |
| File Name      | snake_case           | `video_player.dart`, `wbi_signer.dart` |

### Formatting

| Rule                | Requirement                                      |
| ------------------- | ------------------------------------------------ |
| **Indentation**     | 2 spaces only. NO tabs. NO 4-space.              |
| **Braces**          | Egyptian style (opening brace on same line)      |
| **Trailing Commas** | **REQUIRED** in widget trees and parameter lists |
| **Strings**         | Single quotes `'string'` preferred               |
| **Interpolation**   | Use `'$variable'` not `'+' + variable`           |

### Null Safety

| ✅ DO                          | ❌ DON'T                           |
| ------------------------------ | ---------------------------------- |
| Use `?.` and `??` operators    | Use `!` without prior null check   |
| Declare nullable with `?`      | Assume non-null without validation |
| Handle null in `if` statements | Sprinkle `!` everywhere            |

**The bang operator `!` is FORBIDDEN** unless preceded by explicit null-check logic.

### Performance Optimization

| Rule                           | Rationale                                        |
| ------------------------------ | ------------------------------------------------ |
| Use `const` constructors       | Prevents unnecessary widget rebuilds             |
| Mark stateless widgets `const` | Enables compile-time optimization                |
| Use `const` for fixed literals | `const Text('Hello')`, `const EdgeInsets.all(8)` |

---

## 📂 Project Structure

This is a **Monorepo**. Focus on the `mobile/` directory:

```
mobile/
├── lib/
│   ├── api/              # Network layer
│   │   ├── bilibili_api.dart
│   │   ├── wbi_signer.dart
│   │   └── interceptors/
│   ├── models/           # Data classes (immutable)
│   │   ├── video_model.dart
│   │   ├── audio_info.dart
│   │   └── search_result.dart
│   ├── pages/            # Page-level widgets
│   │   ├── home_page.dart
│   │   ├── search_page.dart
│   │   └── player_page.dart
│   ├── widgets/          # Reusable components
│   │   ├── video_card.dart
│   │   └── audio_player_bar.dart
│   ├── providers/        # State management
│   │   ├── player_provider.dart
│   │   └── search_provider.dart
│   ├── utils/            # Utilities
│   │   └── duration_formatter.dart
│   └── main.dart
```

---

## 🏗️ Class Design Patterns

### Data Class (DTO)

```dart
class VideoModel {
  final String bvid;
  final String title;
  final String author;
  final int duration;
  final int cid;

  const VideoModel({
    required this.bvid,
    required this.title,
    required this.author,
    required this.cid,
    this.duration = 0,
  });

  factory VideoModel.fromJson(Map<String, dynamic> json) {
    return VideoModel(
      bvid: json['bvid'] as String,
      title: json['title'] as String,
      author: json['owner']['name'] as String,
      cid: json['cid'] as int,
      duration: json['duration'] as int? ?? 0,
    );
  }

  bool get isShort => duration < 60;
}
```

### Service Class

```dart
class BilibiliApiService {
  // 1. Static constants
  static const String _baseUrl = 'https://api.bilibili.com';

  // 2. Private fields
  final Dio _dio;
  String? _imgKey;
  String? _subKey;

  // 3. Constructor
  BilibiliApiService() : _dio = Dio() {
    _dio.interceptors.add(BilibiliHeaderInterceptor());
  }

  // 4. Public methods
  Future<void> initialize() async {
    await _fetchWbiKeys();
  }

  Future<List<VideoModel>> searchVideos(String keyword) async {
    // Implementation...
  }

  // 5. Private methods
  Future<void> _fetchWbiKeys() async {
    // Implementation...
  }
}
```

### Widget Class

```dart
class VideoCard extends StatelessWidget {
  final VideoModel video;
  final VoidCallback? onTap;

  const VideoCard({
    super.key,
    required this.video,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return Card(
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                video.title,
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const SizedBox(height: 4),
              Text(
                video.author,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
```

---

## 📥 Import Order

Always follow this order with blank lines between groups:

```dart
// 1. Dart SDK
import 'dart:async';
import 'dart:convert';

// 2. Flutter SDK
import 'package:flutter/material.dart';

// 3. Third-party packages
import 'package:dio/dio.dart';
import 'package:provider/provider.dart';

// 4. Project imports
import '../models/video_model.dart';
import '../api/bilibili_api.dart';
```

---

## ⚠️ Critical Reminders

1. **Audio Stream Headers**: When playing audio via `just_audio`, you MUST set headers:

   ```dart
   await _player.setAudioSource(
     AudioSource.uri(
       Uri.parse(audioUrl),
       headers: {
         'Referer': 'https://www.bilibili.com/',
         'User-Agent': 'Mozilla/5.0 ...',
       },
     ),
   );
   ```

2. **Error Handling**: All API calls must be wrapped in try-catch with proper error types.

3. **JSON Parsing Safety**: Use `as Type?` with fallback, never trust external JSON:

   ```dart
   // ✅ Safe
   final title = json['title'] as String? ?? 'Unknown';

   // ❌ Crash-prone
   final title = json['title'] as String;
   ```

4. **Widget const-ness**: If a widget's constructor can be `const`, it MUST be `const`.

5. **File naming**: All generated Dart files MUST use `snake_case.dart`.

---

## 🧪 Testing Considerations

- Mock `Dio` responses for API tests
- Test WBI signature against known Python outputs
- Verify header injection in all HTTP requests

---

_This instruction file governs all code generation for the BilibiliMusicPlayer project._
