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
        ("password sign-in uses public key and persists tokens without the password", async () =>
        {
            var store = new MemoryStore();
            using var client = new SupabaseAuthClient(store, new Handler(async r =>
            {
                Check(r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/auth/v1/token?grant_type=password");
                Check(r.Headers.GetValues("apikey").Single().StartsWith("sb_publishable_"));
                Check(r.Headers.Authorization is null);
                using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
                Check(body.RootElement.GetProperty("email").GetString() == "a@example.invalid");
                Check(body.RootElement.GetProperty("password").GetString() == "fixture-password");
                return Session(UserA, "a@example.invalid", "access-a", "refresh-a");
            }));
            await client.SignInAsync(" a@example.invalid ", "fixture-password");
            Check(client.Session?.UserId == UserA && store.Session?.RefreshToken == "refresh-a");
            Check(!JsonSerializer.Serialize(store.Session).Contains("fixture-password"));
        }),
        ("sign-up requiring email confirmation never fabricates a logged-in session", async () =>
        {
            var store = new MemoryStore();
            using var client = new SupabaseAuthClient(store, new Handler(r => Task.FromResult(Json(new { id = UserA, email = "a@example.invalid" }))));
            Check(!await client.SignUpAsync("a@example.invalid", "fixture-password"));
            Check(client.Session is null && store.Writes == 0);
        }),
        ("native signup confirmation exchanges an unopened email link and verifies ownership", async () =>
        {
            var store = new MemoryStore();
            using var client = new SupabaseAuthClient(store, new Handler(async r =>
            {
                using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
                Check(r.RequestUri!.AbsolutePath == "/auth/v1/verify");
                Check(body.RootElement.GetProperty("type").GetString() == "signup");
                Check(body.RootElement.GetProperty("token_hash").GetString() == "fixture-hash");
                return Session(UserA, "a@example.invalid", "confirmed", "confirmed-refresh");
            }));
            await client.ConfirmEmailAsync("a@example.invalid", "https://ybprifopdwcorrweanjq.supabase.co/auth/v1/verify?token=fixture-hash&type=signup");
            Check(client.Session?.UserId == UserA && store.Session?.AccessToken == "confirmed");
        }),
        ("default SMTP restrictions and invalid credentials have actionable messages", async () =>
        {
            foreach (var (code, expected) in new[] { ("email_address_not_authorized", "团队邮箱"), ("invalid_credentials", "邮箱或密码"), ("email_not_confirmed", "确认邮箱"), ("over_email_send_rate_limit", "频繁") })
            {
                using var client = new SupabaseAuthClient(new MemoryStore(), new Handler(r => Task.FromResult(Json(new { error_code = code }, HttpStatusCode.BadRequest))));
                await RejectAsync(() => client.SignInAsync("a@example.invalid", "fixture-password"), expected);
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
        ("recovery verifies the original project link without changing the active account", async () =>
        {
            var store = new MemoryStore { Session = new(UserA, "a@example.invalid", "access-a", "refresh-a", DateTimeOffset.UtcNow.AddHours(1)) };
            var calls = 0;
            using var client = new SupabaseAuthClient(store, new Handler(async r =>
            {
                calls++;
                using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
                if (r.Method == HttpMethod.Post)
                {
                    Check(body.RootElement.GetProperty("token_hash").GetString() == "fixture-hash");
                    Check(body.RootElement.GetProperty("type").GetString() == "recovery");
                    return Session(UserB, "b@example.invalid", "recovery-token", "recovery-refresh");
                }
                Check(r.Method == HttpMethod.Put && r.Headers.Authorization?.Parameter == "recovery-token");
                Check(body.RootElement.GetProperty("password").GetString() == "new-fixture-password"); return Json(new { id = UserB });
            }));
            await client.Initialization;
            await client.ResetPasswordAsync("b@example.invalid", "https://ybprifopdwcorrweanjq.supabase.co/auth/v1/verify?token=fixture-hash&type=recovery", "new-fixture-password");
            Check(calls == 2 && client.Session?.UserId == UserA && store.Writes == 0);
        }),
        ("recovery rejects foreign URLs and wrong-purpose links before making requests", async () =>
        {
            var count = 0;
            using var client = new SupabaseAuthClient(new MemoryStore(), new Handler(r => { count++; throw new Exception("Must not send a request"); }));
            await RejectAsync(() => client.ResetPasswordAsync("a@example.invalid", "https://evil.example/auth/v1/verify?token=x&type=recovery", "fixture-password"), "原始确认链接");
            await RejectAsync(() => client.ResetPasswordAsync("a@example.invalid", "https://ybprifopdwcorrweanjq.supabase.co/auth/v1/verify?token=x&type=signup", "fixture-password"), "重置链接");
            Check(count == 0);
        }),
        ("cloud access rejects a different account before sending library data", async () =>
        {
            var store = new MemoryStore { Session = new(UserA, "a@example.invalid", "access-a", "refresh-a", DateTimeOffset.UtcNow.AddHours(1)) };
            using var client = new SupabaseAuthClient(store, new Handler(r => throw new Exception("No auth request expected")));
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
}
