# Bilibili Music Player (mobile)

本目录为 Flutter 客户端实现，覆盖搜索、视频详情、音频播放、收藏与缓存等核心流程。下面按模块说明组件构成、状态转移与界面更新逻辑。

## 架构总览

- UI 通过 Provider 与 StreamBuilder 接入业务状态，核心播放状态由 `audio_service` + `media_kit` 驱动。
- 背景音频处理：`BilibiliAudioHandler` 作为单一事实源，负责播放状态广播、播放队列、播放模式等。
- 所有网络请求走 `Dio`，包含 B 站必需请求头与 WBI 签名。

## 目录结构与职责

- api/
  - B 站 API 客户端、WBI 签名与请求头拦截器。
- models/
  - API 返回数据与本地存储模型，包含视频、详情、播放地址、搜索结果等。
- player/
  - 音频处理器与视频播放器单例管理。
- providers/
  - 搜索、播放、收藏的状态管理与对外接口。
- services/
  - 缓存与下载管理。
- ui/
  - 页面、组件与主题。

## 核心模块说明

### 启动与全局初始化

- `main()` 完成 Hive 初始化、`media_kit` 初始化、缓存管理器初始化与通知权限请求。
- `LibraryProvider` 与 `PlayerProvider` 初始化完成后注入到 `MultiProvider`。
- 主题模式来自 Hive `settings`，通过 `ValueListenableBuilder` 动态切换。

### API 与签名

- `BilibiliClient` 负责初始化 Cookie 与 WBI 密钥，并封装搜索、视频详情、播放地址与建议接口。
- `WbiSigner` 生成 `wts` 与 `w_rid`，对查询参数排序与过滤后计算 MD5 签名。
- `BilibiliHeaderInterceptor` 在请求中注入 B 站所需请求头，避免被拦截。

### 播放核心 (Audio)

- `BilibiliAudioHandler` 继承 `BaseAudioHandler`，持有唯一 `media_kit.Player` 实例。
- 播放流程：
  1.  选择或追加播放列表并设置 `_currentIndex`。
  2.  立即广播 `loading` 状态以更新 UI。
  3.  优先读缓存；未命中则拉取详情与播放地址并触发后台缓存下载。
  4.  使用 `player.open()` 打开媒体并自动播放。

- 播放状态映射：`_broadcastState()` 将 `media_kit` 状态映射为 `audio_service` 的 `PlaybackState`，并控制通知栏按钮布局。
- 播放模式：`loop` / `single` / `shuffle`，在完成回调中切换下一首或单曲重播。
- 渐入渐出：`play()` 与 `pause()` 使用 `_setVolumeWithFade()` 做音量渐变，`_userRequestedPlaying` 用于抑制 UI 闪烁。

### 播放核心 (Video)

- `VideoPlayerManager` 提供全局唯一的 `media_kit.Player` 实例，避免频繁创建销毁原生资源。
- `VideoDetailPage` 进入时暂停后台音频、加载视频详情与播放地址、设置播放器并播放；退出时停止播放并释放音频焦点。

### 缓存管理

- `CacheManager` 负责音频缓存目录创建、下载任务去重、下载进度广播与缓存清理。
- `progressStream` 向 UI 实时推送下载进度，播放器页面据此显示进度环。

## 状态转移说明

### SearchProvider 状态机

`SearchState` 主要转移：

- `idle` → `showingSuggestions`：输入停止 300ms 后获取建议。
- `idle` → `searching`：提交搜索关键字。
- `searching` → `showingResults`：搜索成功。
- `searching` → `error`：搜索失败。
- `showingResults` → `searching`：重新搜索新关键字。
- 其他：`clearSearch()` 回到 `idle`，`refresh()` 保持 `showingResults`。

### 播放状态与 UI

- `_broadcastState()` 基于 `playing`、`buffering`、`position` 推导 `AudioProcessingState`，保证系统控制栏与 UI 同步。
- 播放完成：
  - `single`：回到 0 并继续播放。
  - `loop`：自动跳转下一首或回到开头。
  - 非循环且无下一首：停止并归零。

### 播放列表与队列

- `setPlaylist()` 更新队列但不自动播放（由调用方决定）。
- `addToPlaylist()` / `removeFromPlaylist()` / `clearPlaylist()` 更新队列并同步 `mediaItem`。

## UI 与界面更新逻辑

### 主要页面

- 搜索页：
  - 文本输入触发防抖建议，滚动接近底部自动加载更多。
  - `SearchState` 决定展示历史、建议、结果或错误状态。

- 收藏页：
  - 列表来自 `LibraryProvider.favorites`，支持排序与点击播放。

- 音频播放器页：
  - 通过 `mediaItemStream`、`playbackStateStream` 与 `positionDataStream` 更新封面、按钮与进度条。
  - 监听 `CacheManager.progressStream` 显示下载环。

- 视频详情页：
  - 首屏显示播放器区域，信息区展示标题、UP 主、统计与简介。
  - 进出页面控制视频播放与音频焦点。

- 设置页：
  - 读取/写入 Hive `settings`，驱动主题、渐变播放与缓存清理。

### 关键 UI 更新方式

- `StreamBuilder`：播放状态、媒体信息、播放进度、下载进度全部使用流驱动更新。
- `ChangeNotifier`：搜索与收藏通过 `notifyListeners()` 触发界面刷新。
- `ValueListenableBuilder`：主题模式随 Hive 设置变更实时切换。

## 关键交互流程

### 搜索到播放

1. 搜索页发起 `search()` 获取结果。
2. 点击结果进入详情页或直接播放。
3. 播放器通过 `PlayerProvider` 调用 `playVideo()`，并同步播放列表。

### 收藏与缓存

1. 收藏操作写入 Hive。
2. 触发后台拉取播放地址并下载音频缓存。
3. 播放时优先命中缓存路径，未命中才访问网络。

## 主题与视觉

- `AppTheme` 定义浅色/深色主题与字体。
- `BiliAppBar` 封装统一标题样式与品牌色装饰。
- `MiniPlayer` 固定底部显示，基于播放状态自动显示/隐藏。

## 备注

本实现严格避免轮询，UI 只依赖流或通知驱动更新，保证性能与一致性。
