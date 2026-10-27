using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using bilibili_music_player_windows.Services;

internal static class AccountTests
{
    private static readonly Guid UserA = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid UserB = Guid.Parse("10000000-0000-4000-8000-000000000002");
    public static IEnumerable<(string Name, Func<Task> Run)> All => new (string, Func<Task>)[]
    {
        ("requesting an email OTP auto-registers without sending a password or creating a session", async () =>
        {
            var store = new MemoryStore();
            using var client = new SupabaseAuthClient(store, new Handler(async r =>
            {
                Check(r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/auth/v1/otp");
                Check(r.Headers.GetValues("apikey").Single().StartsWith("sb_publishable_"));
                Check(r.Headers.Authorization is null);
                using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
                Check(body.RootElement.GetProperty("email").GetString() == "a@example.invalid");
                Check(body.RootElement.GetProperty("create_user").GetBoolean());
                Check(!body.RootElement.TryGetProperty("password", out _));
                return Json(new { });
            }));
            await client.RequestEmailCodeAsync(" a@example.invalid ");
            Check(client.Session is null && store.Writes == 0);
        }),
        ("OTP verification uses the email type and persists tokens without the code", async () =>
        {
            var store = new MemoryStore();
            using var client = new SupabaseAuthClient(store, new Handler(async r =>
            {
                Check(r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/auth/v1/verify");
                using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
                Check(body.RootElement.GetProperty("email").GetString() == "a@example.invalid");
                Check(body.RootElement.GetProperty("type").GetString() == "email");
                Check(body.RootElement.GetProperty("token").GetString() == "123456");
                return Session(UserA, "a@example.invalid", "access-a", "refresh-a");
            }));
            await client.VerifyEmailCodeAsync(" a@example.invalid ", " 123456 ");
            Check(client.Session?.UserId == UserA && store.Session?.RefreshToken == "refresh-a");
            Check(!JsonSerializer.Serialize(store.Session).Contains("123456"));
        }),
        ("invalid codes and email display names never reach the server", async () =>
        {
            var count = 0;
            using var client = new SupabaseAuthClient(new MemoryStore(), new Handler(r => { count++; throw new Exception("Unexpected request"); }));
            foreach (var code in new[] { "", "12345", "1234567", "１２３４５６", "123a56", "https://example.invalid/verify" })
                await RejectAsync(() => client.VerifyEmailCodeAsync("a@example.invalid", code), "6 位");
            foreach (var email in new[] { "", "invalid", "Someone <a@example.invalid>" })
            {
                await RejectAsync(() => client.RequestEmailCodeAsync(email), "有效邮箱");
                await RejectAsync(() => client.VerifyEmailCodeAsync(email, "123456"), "有效邮箱");
            }
            Check(count == 0);
        }),
        ("verification cannot replace the current account with a different returned email", async () =>
        {
            var store = SignedInStore();
            using var client = new SupabaseAuthClient(store, new Handler(r => Task.FromResult(Session(UserB, "b@example.invalid", "b", "b-refresh"))));
            await client.Initialization;
            await RejectAsync(() => client.VerifyEmailCodeAsync("a@example.invalid", "123456"), "邮箱不匹配");
            Check(client.Session?.UserId == UserA && store.Writes == 0);
        }),
        ("resend cooldown is case-insensitive, per-email and based on elapsed time", async () =>
        {
            var clock = new Clock();
            var count = 0;
            using var client = new SupabaseAuthClient(new MemoryStore(), new Handler(r => { count++; return Task.FromResult(Json(new { })); }), clock);
            await client.RequestEmailCodeAsync("a@example.invalid");
            Check(client.GetEmailCodeRetryAfter(" A@example.invalid ") == TimeSpan.FromSeconds(60));
            await RejectAsync(() => client.RequestEmailCodeAsync("A@example.invalid"), "60 秒");
            await client.RequestEmailCodeAsync("b@example.invalid");
            clock.Advance(TimeSpan.FromSeconds(61));
            await client.RequestEmailCodeAsync("a@example.invalid");
            Check(count == 3 && client.GetEmailCodeRetryAfter("b@example.invalid") == TimeSpan.Zero);
        }),
        ("double-clicking send cannot produce duplicate in-flight emails", async () =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            using var client = new SupabaseAuthClient(new MemoryStore(), new Handler(async r =>
            {
                count++; started.SetResult(); await finish.Task; return Json(new { });
            }));
            var first = client.RequestEmailCodeAsync("a@example.invalid");
            await started.Task;
            var second = client.RequestEmailCodeAsync("a@example.invalid");
            finish.SetResult();
            await first;
            await RejectAsync(() => second, "秒后");
            Check(count == 1);
        }),
        ("an ambiguous email timeout keeps the cooldown and never retries automatically", async () =>
        {
            var count = 0;
            using var client = new SupabaseAuthClient(new MemoryStore(), new Handler(r =>
            { count++; throw new HttpRequestException("Lost response after send"); }));
            try { await client.RequestEmailCodeAsync("a@example.invalid"); throw new Exception("Expected network failure"); }
            catch (HttpRequestException) { }
            await RejectAsync(() => client.RequestEmailCodeAsync("a@example.invalid"), "秒后");
            Check(count == 1 && client.Session is null);
        }),
        ("server retry-after extends the resend countdown", async () =>
        {
            var clock = new Clock();
            using var client = new SupabaseAuthClient(new MemoryStore(), new Handler(r =>
            {
                var response = Json(new { error_code = "over_email_send_rate_limit" }, HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new(TimeSpan.FromMinutes(5));
                return Task.FromResult(response);
            }), clock);
            await RejectAsync(() => client.RequestEmailCodeAsync("a@example.invalid"), "发送已达限额");
            Check(client.GetEmailCodeRetryAfter("a@example.invalid") == TimeSpan.FromMinutes(5));
            clock.Advance(TimeSpan.FromMinutes(1));
            await RejectAsync(() => client.RequestEmailCodeAsync("a@example.invalid"), "240 秒");
        }),
        ("cancelled OTP verification cannot persist a late session", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var store = SignedInStore();
            using var client = new SupabaseAuthClient(store, new Handler(r =>
            {
                cancellation.Cancel();
                return Task.FromResult(Session(UserB, "b@example.invalid", "b", "b-refresh"));
            }));
            await client.Initialization;
            try { await client.VerifyEmailCodeAsync("b@example.invalid", "123456", cancellation.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
            Check(client.Session?.UserId == UserA && store.Writes == 0);
        }),
        ("expired and incorrect OTPs preserve the existing session", async () =>
        {
            foreach (var code in new[] { "otp_expired", "invalid_credentials" })
            {
                var store = SignedInStore();
                using var client = new SupabaseAuthClient(store, new Handler(r => Task.FromResult(Json(new { error_code = code }, HttpStatusCode.BadRequest))));
                await client.Initialization;
                await RejectAsync(() => client.VerifyEmailCodeAsync("b@example.invalid", "123456"), "验证码不正确");
                Check(client.Session?.UserId == UserA && store.Writes == 0);
            }
        }),
        ("mail service errors have actionable messages without exposing server responses", async () =>
        {
            foreach (var (code, expected) in new[] { ("email_address_not_authorized", "邮件服务"), ("email_not_confirmed", "获取邮箱验证码"), ("over_request_rate_limit", "频繁"), ("signup_disabled", "无法发送验证码") })
            {
                using var client = new SupabaseAuthClient(new MemoryStore(), new Handler(r => Task.FromResult(Json(new { error_code = code }, HttpStatusCode.BadRequest))));
                await RejectAsync(() => client.RequestEmailCodeAsync("a@example.invalid"), expected);
            }
        }),
        ("concurrent expired-session requests rotate the refresh token exactly once", async () =>
        {
            var store = new MemoryStore { Session = new(UserA, "a@example.invalid", "old", "old-refresh", DateTimeOffset.UtcNow.AddMinutes(-1)) };
            var count = 0;
            using var client = new SupabaseAuthClient(store, new Handler(async r =>
            {
                count++; Check(r.RequestUri!.Query == "?grant_type=refresh_token");
                await Task.Delay(20); return Session(UserA, "a@example.invalid", "new", "rotated");
            }));
            var tokens = await Task.WhenAll(client.GetAccessTokenAsync(default), client.GetAccessTokenAsync(default));
            Check(count == 1 && tokens.All(t => t == "new") && store.Session?.RefreshToken == "rotated");
        }),
        ("sign-out cannot be undone by a refresh already in flight", async () =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new MemoryStore { Session = new(UserA, "a@example.invalid", "old", "old-refresh", DateTimeOffset.UtcNow.AddMinutes(-1)) };
            using var client = new SupabaseAuthClient(store, new Handler(async r =>
            {
                if (r.RequestUri!.AbsolutePath.EndsWith("/logout")) return Json(new { });
                started.SetResult(); await finish.Task; return Session(UserA, "a@example.invalid", "new", "rotated");
            }));
            var refresh = client.GetAccessTokenAsync(default); await started.Task;
            var logout = client.SignOutAsync(); finish.SetResult(); await Task.WhenAll(refresh, logout);
            Check(client.Session is null && store.Session is null);
        }),
        ("cloud access rejects a different account before sending library data", async () =>
        {
            using var client = new SupabaseAuthClient(SignedInStore(), new Handler(r => throw new Exception("No auth request expected")));
            await client.Initialization; using var cloud = new SupabaseLibraryCloud(client);
            try { await cloud.WriteAsync(UserB, 0, new(), default); throw new Exception("Wrong account accepted"); }
            catch (OperationCanceledException) { }
        }),
        ("numeric error codes and unreadable local tokens fail safely", async () =>
        {
            using var response = Json(new { code = 400 }, HttpStatusCode.BadRequest);
            await RejectAsync(() => SupabaseAuthClient.CheckResponseAsync(response), "400");
            using var client = new SupabaseAuthClient(new MemoryStore { FailRead = true }, new Handler(r => throw new Exception()));
            await client.Initialization; Check(client.Session is null);
        }),
    };
    private static MemoryStore SignedInStore() => new() { Session = new(UserA, "a@example.invalid", "access-a", "refresh-a", DateTimeOffset.UtcNow.AddHours(1)) };
    private static void Check(bool value) { if (!value) throw new Exception("Account regression failed"); }
    private static async Task RejectAsync(Func<Task> action, string expected)
    { try { await action(); } catch (InvalidOperationException ex) when (ex.Message.Contains(expected)) { return; } throw new Exception("Expected account error: " + expected); }
    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(body) };
    private static HttpResponseMessage Session(Guid id, string email, string token, string refresh) => Json(new
    { access_token = token, refresh_token = refresh, expires_in = 3600, user = new { id, email } });
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
    private sealed class MemoryStore : IAccountSessionStore
    {
        public AccountSession? Session; public int Writes; public bool FailRead;
        public Task<AccountSession?> ReadAsync() => FailRead ? throw new IOException() : Task.FromResult(Session);
        public Task WriteAsync(AccountSession? session) { Session = session; Writes++; return Task.CompletedTask; }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
