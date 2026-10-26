# 收藏、动态歌单与云同步

本期聚焦 Windows。安卓原有收藏还未迁移到新格式。

## 产品规则

- 收藏库包含独立的“我喜欢”、全部收藏、文件夹与歌单。取消喜欢不删除歌单成员。
- 文件夹可以嵌套，只接受文件夹或歌单；删除文件夹是解散，子项目移到上一级。
- 歌单的有序条目可以是音源，也可以是另一歌单的动态引用。引用源更新后，展开结果随本机收藏状态更新；云端修改在同步后反映。
- 文件夹和动态歌单展开后按音源 ID 去重，保留首次出现的位置。不同视频或分 P 不按标题合并。
- 文件夹移动、歌单引用都拒绝自引用、间接循环和超过 64 层的结构。
- 播放全部替换队列并播放；添加到队列追加；下一首播放按原顺序插入。队列保存音源快照，编辑歌单不改变已经排好的内容。
- 切换账号会清空正在播放的队列，收藏库按账号隔离；未登录库保持独立。账户菜单可将本机收藏合并导入当前账号。

## 存储取舍

逻辑对象为媒体、喜欢关系、目录节点（文件夹/歌单）、有序歌单条目。Windows 使用版本化 JSON，并用临时文件、刷盘、原子替换和上一版备份保护本机写入。旧 `favorites.json` 只读迁移一次，原文件保留。

云端 `public.music_libraries` 每账号保存一份 JSONB 图和单调递增的 revision。本期选择整库事务提交，以保证目录、引用、顺序和成员关系不会出现半更新；适合本项目个人级收藏规模，代价是每次同步发送整份元数据。数据库限制单库文档 12MB，后续规模显著增加时可按逻辑对象拆表。

云同步在进入收藏页、手动同步及修改后的短暂防抖后触发。先本机保存，断网仍可编辑。云端版本与本机基线不一致时停止覆盖，提示用户选择版本；被替换的版本保存在账号目录的 `.conflict-local` 或 `.conflict-cloud` 文件中。丢失提交响应时，比较文档识别已经成功的提交。

当前没有跨设备同步播放进度或播放队列。云端只存收藏元数据，不存音频、密码、令牌或临时播放地址。

Windows Release 暂时关闭 IL 裁剪，保留原生绑定与现有 JSON 模型所需的反射元数据。[.NET 8 的裁剪默认会关闭反射序列化](https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/8.0/publishtrimmed)；在所有模型完成源生成迁移之前，以打包后的行为可靠为优先。

## Supabase

项目：`ybprifopdwcorrweanjq`。客户端仅使用公开的 publishable key。

- 账户行通过 `auth.uid()` 与 RLS 隔离，匿名角色没有表访问或写入 RPC 权限。
- 客户端对表仅有 SELECT 权限。写入通过公开的 SECURITY INVOKER 包装函数，调用不暴露于 Data API 的私有命令。
- 私有写入命令从会话推导账号，不接受客户端 owner；账号级事务锁和 revision 检查拒绝旧版本覆盖。
- 服务端独立检查 JSON 格式、ID/源标识去重、悬空关系、循环与深度，不信任客户端验证。
- `supabase/migrations/` 与远端迁移历史版本一致；`supabase/tests/music_library.sql` 在回滚事务里验证隔离、权限和图约束，没有遗留测试账号。

## 邮箱密码登录

邮箱认证、注册、邮箱确认、密码重设和自动刷新令牌均使用 Supabase Auth。不会自行保存密码散列，也不把密码写入本机文件。会话令牌由 Windows `DataProtectionProvider("LOCAL=user")` 加密保存。

此项目已启用邮箱注册和邮箱确认。注册后可将确认邮件的验证码或尚未打开的原始链接粘贴回应用，直接完成邮箱确认；也可通过邮件确认后回来登录。原生确认方式不依赖网站回调或商店上架。找回密码同样支持邮件验证码或原始重置链接；验证后更新密码，恢复用的临时会话不会替换当前账号。

建议将 Supabase 的 Reset Password 邮件模板加入 `{{ .Token }}`，使用户直接输入邮件验证码，无需复制链接。使用默认链接时，应用也提供链接输入方式。

[默认发信服务](https://supabase.com/docs/guides/auth/auth-smtp)仅发给项目团队邮箱，当前限额 2 封/小时。先用项目团队邮箱验证个人使用；向其他用户开放前配置自有 SMTP。没有关闭邮箱确认，也没有改变发信或注册安全配置。

第三方登录不等于必须先上架应用商店。[Google 的开发配置](https://supabase.com/docs/guides/auth/social-sign-in/auth-google)支持本地测试，审核取决于提供方、用户范围、品牌与申请的权限。本期采用已选定的邮箱密码方案。

## 调研边界

没有找到能证明“海外普遍支持嵌套、国内不支持”的完整官方清单，也没有找到能解释这种地域差异的公开对照调研，不能用产品功能差异推出用户需求差异。

- [Spotify 官方：2026 年 5 月开放手机端文件夹管理](https://newsroom.spotify.com/2026-05-28/playlist-folders-mobile-queue-controls-updates/)。同一个产品也长期存在不同终端的功能差异，说明产品历史和终端实现都可能影响功能覆盖。
- [Spotify 官方文件夹说明](https://support.spotify.com/uk/article/playlist-folders/)和[Apple 官方文件夹说明](https://support.apple.com/guide/music/organize-playlists-in-folders-musb6032bb56/mac)确认存在成熟的嵌套目录先例。
- [Understanding Playlist Creation on Music Streaming Platforms，2016](https://dbis.uibk.ac.at/sites/default/files/2018-05/ism2016.pdf)研究 Spotify 用户创建的歌单及其音频特征。这不是中外市场对照，也不能证明用户对嵌套文件夹的普遍偏好。

因此，本项目支持嵌套和动态链接的直接依据是已明确的用户需求，而不是未经证实的地域解释。

## 验证

`dotnet run --project windows.Tests/PlaybackRegressionTests.csproj --no-restore` 覆盖媒体流、搜索、常驻页面、动态图、持久化、同步冲突、账号切换及 Auth 协议。认证协议测试使用内存令牌存储和模拟 HTTP，不发送邮件或操作真实账号。

Debug 检查支持 `Inspect-Ui.ps1 -LibraryFixture sample`，使用独立的随机本机测试档案并禁用云同步；`-LibraryFixture restore` 返回实际账号或本机库。Release 没有这个入口。
