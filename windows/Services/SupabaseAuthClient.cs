using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
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

public sealed class EmailCodeRateLimitException(string message, TimeSpan retryAfter) : InvalidOperationException(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>Email OTP Auth. Only encrypted session tokens are persisted; codes stay in memory.</summary>
public class SupabaseAuthClient : IDisposable
{
    internal const string ProjectUrl = "https://ybprifopdwcorrweanjq.supabase.co";
    internal const string PublishableKey = "sb_publishable_auLuqtCJV4pQ3W1hn2q5KA_xOwJBJtk";
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly SemaphoreSlim _sendCode = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _emailRetryAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;
    private readonly IAccountSessionStore _store;
    public const int EmailCodeLength = 6;
    public static readonly TimeSpan EmailCodeResendDelay = TimeSpan.FromSeconds(60);
    private bool _disposed;
    public AccountSession? Session { get; private set; }
    public event EventHandler? Changed;
    public Task Initialization { get; }

    public SupabaseAuthClient(IAccountSessionStore store, HttpMessageHandler? handler = null, TimeProvider? time = null)
    {
        _store = store;
        _time = time ?? TimeProvider.System;
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

    public TimeSpan GetEmailCodeRetryAfter(string email)
    {
        if (!_emailRetryAt.TryGetValue(email.Trim(), out var retryAt)) return TimeSpan.Zero;
        var remaining = retryAt - _time.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public async Task RequestEmailCodeAsync(string email, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);
        await Initialization;
        await _sendCode.WaitAsync(ct);
        try
        {
            var remaining = GetEmailCodeRetryAfter(email);
            if (remaining > TimeSpan.Zero)
                throw new EmailCodeRateLimitException($"请在 {Math.Ceiling(remaining.TotalSeconds):0} 秒后重新获取验证码。", remaining);
            foreach (var item in _emailRetryAt)
                if (item.Value <= _time.GetUtcNow()) _emailRetryAt.TryRemove(item.Key, out _);
            // Keep the cooldown after a timeout too: the server may already have sent the email.
            _emailRetryAt[email] = _time.GetUtcNow() + EmailCodeResendDelay;
            try
            {
                using var result = await SendAuthAsync("/auth/v1/otp", new { email, create_user = true }, ct);
                _emailRetryAt[email] = _time.GetUtcNow() + EmailCodeResendDelay;
            }
            catch (EmailCodeRateLimitException ex)
            {
                _emailRetryAt[email] = _time.GetUtcNow() + ex.RetryAfter;
                throw;
            }
        }
        finally { _sendCode.Release(); }
    }

    public async Task VerifyEmailCodeAsync(string email, string code, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);
        code = code.Trim();
        if (!IsEmailCode(code)) throw new InvalidOperationException("请输入邮件中的 6 位数字验证码。");
        await Initialization;
        await _refresh.WaitAsync(ct);
        try
        {
            using var result = await SendAuthAsync("/auth/v1/verify", new { email, token = code, type = "email" }, ct);
            var session = ParseSession(result.RootElement);
            if (!string.Equals(session.Email, email, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("验证信息与邮箱不匹配，请重新获取验证码。");
            ct.ThrowIfCancellationRequested();
            await SetSessionAsync(session);
        }
        finally { _refresh.Release(); }
    }

    public static bool IsEmailCode(string code)
    {
        if (code.Length != EmailCodeLength) return false;
        foreach (var character in code) if (character is < '0' or > '9') return false;
        return true;
    }

    private static string NormalizeEmail(string email)
    {
        email = email.Trim();
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new InvalidOperationException("请输入有效邮箱。");
        return email;
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
        if (code is "over_email_send_rate_limit" or "over_request_rate_limit" || (int)response.StatusCode == 429)
        {
            var retry = response.Headers.RetryAfter?.Delta ??
                (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? EmailCodeResendDelay;
            if (retry <= TimeSpan.Zero) retry = EmailCodeResendDelay;
            throw new EmailCodeRateLimitException(code == "over_email_send_rate_limit"
                ? "邮件发送已达限额，请稍后重试。" : "操作过于频繁，请稍后再试。", retry);
        }
        throw new InvalidOperationException(code switch
        {
            "invalid_credentials" or "otp_expired" => "验证码不正确、已过期或已使用，请重新获取验证码。",
            "email_address_invalid" or "validation_failed" => "邮箱格式不正确，请检查后重试。",
            "email_not_confirmed" => "请重新获取邮箱验证码完成验证。",
            "email_address_not_authorized" or "email_provider_disabled" => "邮件服务暂不可用，请稍后重试。",
            "signup_disabled" or "otp_disabled" => "暂时无法发送验证码，请稍后重试。",
            "refresh_token_not_found" or "refresh_token_already_used" or "session_not_found" or "bad_jwt" => "登录已过期，请重新登录。",
            _ when response.StatusCode == HttpStatusCode.Unauthorized => "登录已过期，请重新登录。",
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

    public void Dispose() { if (_disposed) return; _disposed = true; _http.Dispose(); _refresh.Dispose(); _sendCode.Dispose(); }
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

    public Task<LibrarySyncReply> PullAsync(Guid owner, Guid writer, long revision, string hash, bool snapshot, CancellationToken ct) =>
        SendAsync(owner, "music_pull_v2", new { p_writer = writer, p_revision = revision, p_hash = hash, p_snapshot = snapshot }, ct);

    public Task<LibrarySyncReply> SubmitAsync(Guid owner, Guid writer, long revision, string hash, IReadOnlyList<LibraryCommand> commands, CancellationToken ct) =>
        SendAsync(owner, "music_submit_v2", new { p_writer = writer, p_revision = revision, p_hash = hash, p_operations = commands }, ct);

    private async Task<LibrarySyncReply> SendAsync(Guid owner, string method, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/rpc/" + method)
        { Content = JsonContent.Create(body, options: LibraryJson.Options) };
        request.Headers.Authorization = new("Bearer", await TokenForAsync(owner, ct));
        using var response = await _http.SendAsync(request, ct);
        await SupabaseAuthClient.CheckResponseAsync(response);
        return await response.Content.ReadFromJsonAsync<LibrarySyncReply>(LibraryJson.Options, ct)
            ?? throw new JsonException("Missing library reply.");
    }
    public void Dispose() => _http.Dispose();
}
