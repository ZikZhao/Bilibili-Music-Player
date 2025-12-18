# Bilibili Music Player - 跨平台音乐播放器 (Flutter)

一个基于 **Flutter** 构建的现代化、跨平台 Bilibili 音频播放客户端。该项目纯属个人兴趣驱动的创意实践，旨在打破视频流与纯音频体验的壁垒，实现从 Bilibili 视频源到高质量音乐播放器的无缝转换。它展示了移动端架构设计、复杂 API 逆向（Wbi 签名）、后台音频服务管理以及 Material Design 界面交互的综合应用。

------

## 🚀 主要亮点

### 1. 沉浸式音频流媒体架构

通过自定义的音频处理管道，实现了从视频平台到纯净音乐播放器的转换，支持无缝的后台播放体验。

- **后台播放服务 (Audio Handler):** 基于 `audio_service` 和 `just_audio` 封装的核心播放逻辑，支持锁屏控制、通知栏交互以及耳机线控，确保像原生音乐 App 一样的体验。
- **流媒体解析:** 实现了高效的视频流转音频流解析逻辑，直接从 Bilibili 源获取最佳音质数据，无需本地转码，即点即播。
- **状态管理:** 采用 **Provider** 模式管理全局播放状态、播放列表（Playlist）及 UI 响应，确保应用在高频交互下的流畅性（60fps）。

### 2. 深度 Bilibili 生态集成 (API Client)

完整复刻了 Bilibili 核心功能的网络层，并在移动端实现了复杂的安全签名机制。

- **Wbi 签名算法:** 在 `mobile/lib/api/wbi_signer.dart` 中原生实现了 Bilibili 最新的 Wbi 鉴权算法，解决了 API 请求的签名验证问题，确保搜索与播放接口的稳定性。
- **拦截器机制:** 定制的 `BilibiliHeaderInterceptor` 自动管理 Cookie、User-Agent 和 Referer，模拟标准浏览器行为以绕过防盗链限制。
- **全能搜索与库:** 支持关键词联想搜索 (`SuggestionModel`)、视频详情抓取以及用户个人收藏库的同步。

### 3. 精致的交互式 UI 系统

遵循 Material Design 规范，注重细节打磨，提供视觉与操作的双重愉悦。

- **Mini Player & 详情页:** 实现了类似主流音乐软件的底部迷你播放器，支持平滑过渡到全屏歌词/封面页（`AudioPlayerPage`）。
- **动态视觉效果:** 针对长标题实现了跑马灯效果 (`MarqueeText`)，并根据系统设置自动适配深色模式（Dark Mode）。

------

## 📋 技术说明

### 功能支持 & 格式

| **功能模块**       | **实现状态**  | **备注**                                                     |
| ------------------ | ------------- | ------------------------------------------------------------ |
| **核心音频播放器** | ✅ 稳定        | 支持来自 Bilibili Dash API `m4a`, `mp3` 的流                 |
| **搜索模块**       | ✅ Wbi 签名    | 实时关键词建议 & 防抖搜索                                    |
| **收藏模块**       | 🚧 仍在优化    | 目前仅支持本地手动添加，计划实现基于数据库的同步和 Bilibili 账号收藏夹读取 |
| **跨平台支持**     | Android / iOS | 电脑端计划开发中                                             |

### API 校验实现

特别感谢以下开源项目提供的 API 文档参考，为本项目的开发提供了巨大的帮助：

- **[SocialSisterYi/bilibili-API-collect](https://github.com/SocialSisterYi/bilibili-API-collect)**: 哔哩哔哩 API 收集整理项目。本应用的 Wbi 签名算法与核心 API 请求逻辑均参考了该仓库的详细文档。

Bilibili 的 API 接口包含复杂的 Wbi 签名校验。本项目并未依赖庞大的第三方 Python 脚本，而是直接在 Dart 层实现了以下流程：

1. **Mixin Key 获取:** 动态获取并计算 img_key 和 sub_key。
2. **重组混淆表:** 在本地通过算法还原混合密钥。
3. **参数签名:** 对请求参数进行排序、拼接并计算 MD5 哈希。

*相关代码位于 `lib/api/wbi_signer.dart`，展示了纯 Dart 处理复杂加密逻辑的能力。*

### 项目结构

项目采用清晰的分层架构（Layered Architecture），便于维护和扩展：

- `mobile`: 手机端

  - `lib/api/`: 网络请求、拦截器与签名算法。

  - `lib/models/`: JSON 序列化数据模型（使用 `json_serializable`）。

  - `lib/providers/`: 业务逻辑与状态管理。

  - `lib/ui/`: 视图层，包含原子化组件（Widgets）与页面（Pages）。

  - `lib/player/`: 核心播放器服务与音频焦点控制。

------

## 🛠️ 入门指南

### 前置条件

确保本地环境已配置 Flutter SDK (>=3.0.0)。

### 安装 & 运行

```bash
# 1. Clone the repository
git clone https://github.com/zikzhao/bilibili-music-player.git

# 2. Install dependencies
cd mobile
flutter pub get

# 3. Run code generation (for JSON models)
dart run build_runner build

# 4. Start the app
flutter run
```
