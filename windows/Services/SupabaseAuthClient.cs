using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Services;

public sealed record AccountSession(Guid UserId, string Email, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public interface IAccountSessionStore
{
    Task<AccountSession?> ReadAsync();
    Task WriteAsync(AccountSession? session);
}

/// <summary>Public-client Auth. Only encrypted session tokens are persisted, never passwords.</summary>
public class SupabaseAuthClient : IDisposable
{
    internal const string ProjectUrl = "https://ybprifopdwcorrweanjq.supabase.co";
    internal const string PublishableKey = "sb_publishable_auLuqtCJV4pQ3W1hn2q5KA_xOwJBJtk";
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly IAccountSessionStore _store;
    private bool _disposed;
    public AccountSession? Session { get; private set; }
    public event EventHandler? Changed;
    public Task Initialization { get; }

    public SupabaseAuthClient(IAccountSessionStore store, HttpMessageHandler? handler = null)
    {
        _store = store;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(ProjectUrl); _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.Add("apikey", PublishableKey);
        Initialization = RestoreAsync();
    }

    private async Task RestoreAsync()
    {
        try
        {
            Session = await _store.ReadAsync();
            if (Session?.UserId == Guid.Empty) Session = null;
        }
        catch { Session = null; }
    }

    public async Task SignInAsync(string email, string password)
    {
        await Initialization;
        await _refresh.WaitAsync();
        try
        {
            using var json = await SendAuthAsync("/auth/v1/token?grant_type=password", new { email = email.Trim(), password });
            await SetSessionAsync(ParseSession(json.RootElement));
        }
        finally { _refresh.Release(); }
    }

    public async Task<bool> SignUpAsync(string email, string password)
    {
        await Initialization;
        await _refresh.WaitAsync();
        try
        {
            using var json = await SendAuthAsync("/auth/v1/signup", new { email = email.Trim(), password });
            if (!json.RootElement.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String) return false;
            await SetSessionAsync(ParseSession(json.RootElement));
            return true;
        }
        finally { _refresh.Release(); }
    }

    public async Task SendRecoveryAsync(string email) { using var result = await SendAuthAsync("/auth/v1/recover", new { email = email.Trim() }); }

    public async Task ConfirmEmailAsync(string email, string codeOrLink)
    {
        object verification;
        if (Uri.TryCreate(codeOrLink.Trim(), UriKind.Absolute, out var link))
        {
            var query = ParseQuery(link.Query);
            if (link.Scheme != Uri.UriSchemeHttps || link.Host != new Uri(ProjectUrl).Host || link.AbsolutePath != "/auth/v1/verify" ||
                !query.TryGetValue("token", out var token) || !query.TryGetValue("type", out var type) || type is not ("signup" or "email"))
                throw new InvalidOperationException("请粘贴本应用注册确认邮件中的原始链接。");
            verification = new { token_hash = token, type };
        }
        else verification = new { email = email.Trim(), token = codeOrLink.Trim(), type = "email" };
        await Initialization;
        await _refresh.WaitAsync();
        try
        {
            using var result = await SendAuthAsync("/auth/v1/verify", verification);
            var session = ParseSession(result.RootElement);
            if (!string.Equals(session.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("确认信息与邮箱不匹配。");
            await SetSessionAsync(session);
        }
        finally { _refresh.Release(); }
    }

    public async Task ResetPasswordAsync(string email, string codeOrLink, string password)
    {
        object verification;
        if (Uri.TryCreate(codeOrLink.Trim(), UriKind.Absolute, out var link))
        {
            if (link.Host != new Uri(ProjectUrl).Host || link.AbsolutePath != "/auth/v1/verify")
                throw new InvalidOperationException("请粘贴本应用重置邮件中的原始确认链接。");
            var query = ParseQuery(link.Query);
            if (!query.TryGetValue("token", out var token) || !query.TryGetValue("type", out var type) || type != "recovery")
                throw new InvalidOperationException("这不是密码重置链接。");
            verification = new { token_hash = token, type = "recovery" };
        }
        else verification = new { email = email.Trim(), token = codeOrLink.Trim(), type = "recovery" };
        using var response = await SendAuthAsync("/auth/v1/verify", verification);
        var temporary = ParseSession(response.RootElement);
        if (!string.Equals(temporary.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("验证信息与邮箱不匹配。");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/auth/v1/user") { Content = JsonContent.Create(new { password }) };
        request.Headers.Authorization = new("Bearer", temporary.AccessToken);
        using var result = await _http.SendAsync(request);
        await CheckResponseAsync(result);
        // A recovery token never replaces the currently signed-in account.
    }

    private static System.Collections.Generic.Dictionary<string, string> ParseQuery(string query)
    {
        var result = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var pair in query.TrimStart('?').Split('&'))
        { var parts = pair.Split('=', 2); if (parts.Length == 2) result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]); }
        return result;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        await Initialization;
        await _refresh.WaitAsync(ct);
        try
        {
            var current = Session ?? throw new InvalidOperationException("请先登录。");
            if (current.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return current.AccessToken;
            using var json = await SendAuthAsync("/auth/v1/token?grant_type=refresh_token", new { refresh_token = current.RefreshToken }, ct);
            var next = ParseSession(json.RootElement);
            if (next.UserId != current.UserId) throw new InvalidOperationException("登录账号发生变化，请重新登录。");
            // Do not revive a session if the user signed out during refresh.
            if (Session?.UserId != current.UserId) throw new OperationCanceledException();
            await SetSessionAsync(next, notify: false);
            return next.AccessToken;
        }
        finally { _refresh.Release(); }
    }

    public async Task SignOutAsync()
    {
        await _refresh.WaitAsync();
        try
        {
            var old = Session;
            await SetSessionAsync(null);
            if (old is null) return;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/v1/logout?scope=local");
                request.Headers.Authorization = new("Bearer", old.AccessToken);
                using var response = await _http.SendAsync(request);
            }
            catch (HttpRequestException) { } // Local sign-out must work offline.
            catch (TaskCanceledException) { }
        }
        finally { _refresh.Release(); }
    }

    private async Task SetSessionAsync(AccountSession? session, bool notify = true)
    {
        await _store.WriteAsync(session);
        Session = session;
        if (notify) Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<JsonDocument> SendAuthAsync(string path, object body, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(path, body, ct);
        await CheckResponseAsync(response);
        var text = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    internal static async Task CheckResponseAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync();
        string? code = null;
        try { using var json = JsonDocument.Parse(text); if (json.RootElement.TryGetProperty("error_code", out var c) || json.RootElement.TryGetProperty("code", out c)) code = c.ValueKind == JsonValueKind.String ? c.GetString() : c.GetRawText(); }
        catch (JsonException) { }
        if (code == "40001") throw new LibraryConflictException();
        throw new InvalidOperationException(code switch
        {
            "invalid_credentials" => "邮箱或密码不正确。",
            "email_not_confirmed" => "请先通过邮件确认邮箱，再返回登录。",
            "email_address_not_authorized" => "项目尚未配置对外邮件服务，目前请使用项目团队邮箱注册。",
            "over_email_send_rate_limit" or "over_request_rate_limit" => "操作过于频繁，请稍后再试。",
            "user_already_exists" => "该邮箱已经注册，请直接登录。",
            "otp_expired" => "验证信息已过期或已使用，请重新发送邮件。",
            "refresh_token_not_found" or "refresh_token_already_used" or "session_not_found" or "bad_jwt" => "登录已过期，请重新登录。",
            _ when response.StatusCode == HttpStatusCode.Unauthorized => "登录已过期，请重新登录。",
            _ when (int)response.StatusCode == 429 => "操作过于频繁，请稍后再试。",
            _ => $"请求未完成（{(int)response.StatusCode}），请稍后重试。",
        });
    }

    private static AccountSession ParseSession(JsonElement root)
    {
        var user = root.GetProperty("user");
        return new(Guid.Parse(user.GetProperty("id").GetString()!), user.GetProperty("email").GetString() ?? "",
            root.GetProperty("access_token").GetString()!, root.GetProperty("refresh_token").GetString()!,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt64()));
    }

    public void Dispose() { if (_disposed) return; _disposed = true; _http.Dispose(); _refresh.Dispose(); }
}

public sealed class SupabaseLibraryCloud : ILibraryCloud, IDisposable
{
    private readonly SupabaseAuthClient _account;
    private readonly HttpClient _http = new() { BaseAddress = new Uri(SupabaseAuthClient.ProjectUrl), Timeout = TimeSpan.FromSeconds(25) };
    public SupabaseLibraryCloud(SupabaseAuthClient account)
    { _account = account; _http.DefaultRequestHeaders.Add("apikey", SupabaseAuthClient.PublishableKey); }

    private async Task<string> TokenForAsync(Guid owner, CancellationToken ct)
    {
        if (_account.Session?.UserId != owner) throw new OperationCanceledException();
        var token = await _account.GetAccessTokenAsync(ct);
        ct.ThrowIfCancellationRequested();
        if (_account.Session?.UserId != owner) throw new OperationCanceledException();
        return token;
    }

    public async Task<CloudLibrary> ReadAsync(Guid owner, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/rest/v1/music_libraries?select=revision,document");
        request.Headers.Authorization = new("Bearer", await TokenForAsync(owner, ct));
        using var response = await _http.SendAsync(request, ct);
        await SupabaseAuthClient.CheckResponseAsync(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (json.RootElement.GetArrayLength() == 0) return new(0, new());
        var row = json.RootElement[0];
        var doc = row.GetProperty("document").Deserialize<LibraryDocument>(LibraryJson.Options) ?? throw new JsonException();
        LibraryGraph.Validate(doc);
        return new(row.GetProperty("revision").GetInt64(), doc);
    }

    public async Task<CloudLibrary> WriteAsync(Guid owner, long expectedRevision, LibraryDocument document, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/rpc/save_music_library")
        { Content = JsonContent.Create(new { expected_revision = expectedRevision, library_document = document }, options: LibraryJson.Options) };
        request.Headers.Authorization = new("Bearer", await TokenForAsync(owner, ct));
        using var response = await _http.SendAsync(request, ct);
        await SupabaseAuthClient.CheckResponseAsync(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return new(json.RootElement.GetProperty("revision").GetInt64(), document);
    }
    public void Dispose() => _http.Dispose();
}
