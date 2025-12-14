# Dart/Flutter Coding Standards

## 1\. 命名规范 (Naming Conventions)

核心原则：遵循 [Effective Dart](https://dart.dev/guides/language/effective-dart/style) 标准。Dart 区分类型（Type）和变量（Variable）。

| **类型**               | **风格**             | **示例**                         | **备注**                                  |
| :--------------------- | :------------------- | :------------------------------- | :---------------------------------------- |
| **类 (Class)**         | PascalCase           | `VideoPlayer`, `AuthService`     | 大驼峰                                    |
| **类型定义 (Typedef)** | PascalCase           | `VoidCallback`, `JsonMap`        |                                           |
| **枚举类型 (Enum)**    | PascalCase           | `PlayState`                      |                                           |
| **枚举值**             | lowerCamelCase       | `playing`, `paused`, `stopped`   | **注意**：Dart 新版推荐小驼峰，不同于 C++ |
| **局部变量**           | lowerCamelCase       | `videoUrl`, `currentIndex`       | 小驼峰                                    |
| **类成员属性 (公有)**  | lowerCamelCase       | `controller`, `videoList`        | 无下划线                                  |
| **类成员属性 (私有)**  | `_` + lowerCamelCase | `_isLoading`, `_engine`          | **必须**以前缀 `_` 开头                   |
| **常量 (const/final)** | lowerCamelCase       | `defaultTimeout`, `maxRetries`   | Dart 偏好小驼峰，除了特殊数学常量         |
| **函数/方法**          | lowerCamelCase       | `fetchData()`, `buildItem()`     | 无论静态还是实例方法                      |
| **文件名**             | snake_case           | `video_player.dart`, `main.dart` | 保持全小写 + 下划线                       |
| **导入前缀 (as)**      | snake_case           | `import '...' as http_client;`   |                                           |

## 2\. 数据模型与逻辑 (Data Class vs Logic Class)

Dart 没有 `struct`，一切皆为 `class`。但我们在设计模式上区分 **数据类 (Data Class)** 和 **逻辑类 (Service/Widget)**。

### 2.1 Data Class (对应 C++ Struct)

- **用途**：纯数据传输对象（DTO），通常对应 API 返回的 JSON。
- **限制**：
  - 属性通常应为 `final`（不可变性）。
  - **必须**定义构造函数（通常是命名参数构造函数）。
  - 逻辑仅限于数据转换（如 `fromJson`）或简单的格式化 getter。
  - 推荐使用 `equatable` 或 `freezed` 库来处理比较逻辑。

<!-- end list -->

```dart
class VideoModel {
  // 属性：推荐使用 final
  final String bvid;
  final String title;
  final String author;
  final int duration;

  // 构造函数：使用命名参数 + required
  const VideoModel({
    required this.bvid,
    required this.title,
    required this.author,
    this.duration = 0,
  });

  // 允许：工厂构造函数 (Factory) 用于 JSON 解析
  factory VideoModel.fromJson(Map<String, dynamic> json) {
    return VideoModel(
      bvid: json['bvid'],
      title: json['title'],
      author: json['owner']['name'],
    );
  }

  // 允许：简单的 Getter
  bool get isShort => duration < 60;
}
```

### 2.2 Logic Class / Widget (对应 C++ Class)

- **用途**：包含业务逻辑、状态管理或 UI 渲染。
- **定义顺序**：
  1.  静态常量 (`static const`)
  2.  成员属性 (Final fields first)
  3.  构造函数
  4.  生命周期方法 (如 `initState`, `dispose`)
  5.  业务逻辑方法
  6.  `build` 方法 (如果是 Widget)

<!-- end list -->

```dart
class PlayerService {
  // 1. 静态常量
  static const int defaultVolume = 50;

  // 2. 私有属性 (对应 C++ 的 width_)
  // Dart 强制使用 _ 前缀来实现 private
  late final AudioPlayer _player;
  bool _isPlaying = false;

  // 3. 构造函数
  PlayerService() {
    _init();
  }

  // 5. 业务方法 (lowerCamelCase)
  Future<void> _init() async {
    _player = AudioPlayer();
    // ...
  }

  Future<void> playAudio(String url) async {
    await _player.setUrl(url);
    _isPlaying = true;
  }
}
```

## 3\. 格式排版 (Formatting)

### 3.1 缩进与大括号

- **缩进**：严格使用 **2 个空格**。禁止使用 4 个空格或 Tab。
- **大括号**：**跟随行** (Egyptian Style)。
- **尾随逗号 (Trailing Commas)**：在参数列表、Widget 树中，**必须**在最后一个参数后加逗号。这能让格式化工具自动把代码排版成树状结构，极提升可读性。

<!-- end list -->

```dart
// 推荐
Widget build(BuildContext context) {
  return Column(
    children: [
      Text('Title'),
      Icon(Icons.play_arrow), // 加上逗号
    ], // 加上逗号
  ); // 加上逗号
}
```

### 3.2 字符串

- 优先使用 **单引号** `'string'`，除非字符串内包含单引号。
- 使用字符串插值 `'$variable'` 而不是 `+` 拼接。

<!-- end list -->

```dart
// ✅ 正确
final path = 'api/v1/user';
final greeting = 'Hello, $name';

// ❌ 错误
final path = "api/v1/user";
final greeting = 'Hello, ' + name;
```

## 4\. 文件与结构 (Files)

### 4.1 导入顺序 (Import Order)

1.  **Dart SDK** (`import 'dart:async';`)
2.  **Flutter/第三方库** (`import 'package:flutter/material.dart';`)
3.  **项目内文件** (`import 'model/video.dart';`) -\> 推荐使用相对路径或 package 路径统一

### 4.2 目录结构建议

对应你的项目规划：

```text
lib/
├── api/          # 网络层 (Dio, WbiSigner)
├── models/       # Data Classes (VideoModel)
├── pages/        # 页面级 Widget (SearchPage)
├── widgets/      # 通用小组件 (VideoCard)
└── utils/        # 工具类
```

## 5\. 现代 Dart 特性 (Modern Dart)

- **空安全 (Null Safety)**：
  - 默认变量不可为空。
  - 可能为空的变量必须加 `?` (如 `String? error`).
  - **严禁**滥用 `!` (Bang operator)，除非你 100% 确定不为空。
- **异步编程**：
  - 优先使用 `async/await`，避免过多的 `.then()` 回调地狱。
  - 涉及到 I/O 操作必须处理 `try-catch`。
- **箭头函数 (Arrow Syntax)**：
  - 仅用于单行返回的函数。
  - ✅ `int sum(int a, int b) => a + b;`
  - ❌ 避免在长逻辑中使用箭头函数。
- **const 构造**：
  - 对于 UI 组件，如果参数是固定的，**必须**加 `const`。这对 Flutter 性能优化至关重要（避免重绘）。
  - e.g., `const Text('Hello')`.
- **Lint 规则**：
  - 也就是你提到的 "AI 开发顺手生成配置"。在 `analysis_options.yaml` 中，确保开启 `include: package:flutter_lints/flutter.yaml`。
