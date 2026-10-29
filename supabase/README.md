# Supabase Auth 配置

目标项目 `ybprifopdwcorrweanjq`。客户端只使用公开 publishable key；SMTP 授权码只保存在 Supabase 的 SMTP Settings。不要将授权码或管理令牌放入配置示例、模板、迁移、日志或客户端。

## 邮箱验证码

Authentication → Sign In / Providers：开启 Email、Allow new users to sign up、Confirm email。Email OTP length 为 `6`，Email OTP expiration 为 `600` 秒。其他登录提供方和匿名登录保持关闭。

Authentication → Emails → SMTP Settings：启用自定义 SMTP，Host 为 `smtp.qq.com`、Port 为 `465`、Sender name 为 `BiliMusic`。发件地址和 Username 为同一个已授权 QQ 邮箱，Password 为该邮箱的 SMTP 授权码；这些私人配置在控制台填写。

Authentication → Emails → Templates：Confirm sign up 与 Magic link or OTP 都使用本目录 `templates/email-otp.html`，主题为 `BiliMusic 邮箱登录验证码`。正文使用 `{{ .Token }}`，不使用 `{{ .ConfirmationURL }}`。只修改 Magic Link 模板会遗漏新用户首次验证邮件。

Authentication → Rate Limits：Rate limit for sending emails 为 `30` 封/小时，Minimum interval per user 为 `60` 秒；保留验证端点、登录请求和刷新端点的既有 IP 限制。QQ 自身仍可能另行限流，Supabase 的额度不代表 QQ 的投递承诺。

## 拒绝密码会话

应用 `email_otp_only` 数据库迁移后，在 Authentication → Auth Hooks 添加并启用 Customize Access Token (JWT) Claims：类型为 Postgres、Schema 为 `music_auth`、Function 为 `enforce_email_otp`，对应 URI 为 `pg-functions://postgres/music_auth/enforce_email_otp`。

该函数只允许 `otp`、`email/signup`，或 AMR 包含上述验证记录的 `token_refresh`。密码、密码恢复、缺少邮箱、匿名会话和未知方式均返回 403；不修改 claims，也不删除用户或收藏。旧密码会话的访问令牌到期后不能继续刷新，需要重新输入邮箱验证码。

函数只向 `supabase_auth_admin` 授予 usage/execute，不暴露为客户端 RPC，不使用 SECURITY DEFINER。不要为此开启付费 Password Verification Attempt Hook。

## 验证

- `tests/email_otp.sql`：事务内验证允许/拒绝的方式、claims 保持原样以及最小权限，无持久化修改。
- `tests/music_library.sql`：关系表、操作重放、幂等、ABA、递归删除、时间线和 RLS 的事务内回归。
- Windows 的 `AccountTests.cs`：使用模拟 HTTP 验证 OTP 协议、倒计时与账户隔离，不发送真实邮件。
- 实际发信应从客户端请求 OTP，经 Supabase Auth → QQ SMTP 投递；只直接测试 QQ SMTP 不能证明 Supabase 的配置已经生效。

收藏协议使用 `music_pull_v2`／`music_submit_v2`，Windows 本机为 SQLite，云端为关系表。当前开发版数据已一次性转换，`music_libraries` 仅留作备份，旧保存接口不再接受写入。规则与日志保留窗口见 [同步实现](../docs/sync-proposal.md)。
