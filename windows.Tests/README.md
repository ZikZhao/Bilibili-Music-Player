# Windows 播放与搜索回归检查

从仓库根目录运行：

```powershell
dotnet run --project windows.Tests/PlaybackRegressionTests.csproj
dotnet build windows/bilibili-music-player-windows.csproj -p:Platform=x64
```

回归程序直接编译播放器使用的 `HttpRangeStream.cs`，通过模拟 HTTP 服务器验证读取字节、前后拖动、EOF、CDN 请求头、忽略 Range 的服务器、分段响应、取消加载、读取期间关闭、无效分段、提前断流和并发读取。无需测试框架、B 站账号或联网；任一检查失败时返回非零退出码。

`SearchSessionTests.cs` 直接编译应用使用的搜索会话，验证输入只获取候选词、提交时立即取消候选词、晚到响应不能覆盖新请求、分页隔离、加载中回退、预览换片只有一个历史条目、矩阵滚动位置恢复，以及跨页面保留预览。使用可控的异步响应，测试不依赖 B 站网络速度。

`ResidentTabsTests.cs` 验证顶层 tab 使用真正的实例驻留：反复往返只创建一次页面，隐藏不释放页面及其资源，重复选中当前 tab 不打断播放，关闭窗口时统一释放；一个页面清理失败也不能阻止其他页面释放。

界面验证请在 Visual Studio 部署并运行最新的 Windows 项目。仅执行 `dotnet build` 不会刷新之前部署到 `AppX` 目录的应用。搜索页的回退顺序为“预览 → 原矩阵结果 → 初始历史页”，窗体左上角和 Alt+← 共用同一条历史。

顶层 tab 由 `MainWindow.PageHost` 常驻托管，不使用 `Frame.Navigate`。播放中切到收藏再返回，应当直接显示同一个播放器的暂停画面，保留进度、缓冲、音量与倍速，不出现重新获取视频的加载圈。加载过程中切走也应继续准备媒体，完成后保持暂停。只有离开预览状态、换片、播放失败或关闭窗口时才释放对应媒体。

还应检查候选词的鼠标选择、方向键选择、Enter 和放大镜提交、失败重试，以及窄窗口和明暗主题。
