using System;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using bilibili_music_player_windows.Services;

namespace bilibili_music_player_windows.Controls;

/// <summary>Native email OTP sign-in. First-time users follow the same flow.</summary>
public sealed class AccountDialog : ContentDialog
{
    private readonly SupabaseAccountService _account = App.Current.GetService<SupabaseAccountService>();
    private readonly TextBox _email = new() { Header = "邮箱", PlaceholderText = "name@example.com", MaxLength = 254 };
    private readonly TextBox _verification = new()
    {
        Header = "验证码", PlaceholderText = "邮件中的 6 位数字", MaxLength = SupabaseAuthClient.EmailCodeLength,
        InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } },
    };
    private readonly Button _send = new() { Content = "获取验证码" };
    private readonly ProgressRing _progress = new() { Width = 16, Height = 16, IsActive = false, Visibility = Visibility.Collapsed };
    private readonly InfoBar _notice = new()
    {
        IsOpen = false, Visibility = Visibility.Collapsed, IsClosable = false,
        Severity = InfoBarSeverity.Error, MaxWidth = 430,
    };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _busy;

    public AccountDialog()
    {
        Title = "邮箱验证码登录"; PrimaryButtonText = "登录"; CloseButtonText = "取消";
        DefaultButton = ContentDialogButton.Primary;
        _email.Text = _account.Session?.Email ?? "";
        var panel = new StackPanel { Spacing = 14, MinWidth = 300, MaxWidth = 440 };
        panel.Children.Add(new TextBlock
        {
            Text = "首次验证会自动创建账户。登录后同步收藏、歌单和文件夹。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(_email);
        var sendRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        sendRow.Children.Add(_send); sendRow.Children.Add(_progress);
        panel.Children.Add(sendRow); panel.Children.Add(_verification); panel.Children.Add(_notice);
        Content = panel;
        AutomationProperties.SetAutomationId(this, "AccountDialog");
        AutomationProperties.SetAutomationId(_email, "AccountEmail");
        AutomationProperties.SetAutomationId(_verification, "AccountVerification");
        AutomationProperties.SetAutomationId(_send, "AccountSendCode");
        _email.TextChanged += (_, _) =>
        {
            _verification.Text = "";
            _notice.IsOpen = false; _notice.Visibility = Visibility.Collapsed;
            UpdateActions();
        };
        _verification.TextChanged += (_, _) => UpdateActions();
        _send.Click += async (_, _) => await SendCodeAsync();
        _timer.Tick += (_, _) => UpdateActions();
        PrimaryButtonClick += OnPrimaryClick;
        Opened += (_, _) => { _timer.Start(); UpdateActions(); _email.Focus(FocusState.Programmatic); };
        Closed += (_, _) => { _timer.Stop(); _verification.Text = ""; };
        // A token response must not switch accounts after the dialog is dismissed.
        Closing += (_, args) => { if (_busy) args.Cancel = true; };
        UpdateActions();
    }

    private bool ValidEmail() => MailAddress.TryCreate(_email.Text.Trim(), out var address) && address.Address == _email.Text.Trim();

    private void Notice(string text, bool success = false)
    {
        _notice.Message = text;
        _notice.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        _notice.Visibility = Visibility.Visible; _notice.IsOpen = true;
    }

    private void UpdateActions()
    {
        var retry = _account.GetEmailCodeRetryAfter(_email.Text);
        _send.Content = _busy ? "请稍候…" : retry > TimeSpan.Zero
            ? $"{Math.Ceiling(retry.TotalSeconds):0} 秒后重发" : "获取验证码";
        _send.IsEnabled = !_busy && ValidEmail() && retry <= TimeSpan.Zero;
        IsPrimaryButtonEnabled = !_busy && ValidEmail() && SupabaseAuthClient.IsEmailCode(_verification.Text.Trim());
        _email.IsEnabled = _verification.IsEnabled = !_busy;
        _progress.IsActive = _busy;
        _progress.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Busy(bool value) { _busy = value; UpdateActions(); }

    private async Task SendCodeAsync()
    {
        if (_busy) return;
        if (!ValidEmail()) { Notice("请输入有效邮箱。"); return; }
        Busy(true);
        try
        {
            await _account.RequestEmailCodeAsync(_email.Text);
            _verification.Text = "";
            Notice("验证码已发送，10 分钟内有效。请查看收件箱或垃圾邮件。", true);
        }
        catch (Exception ex) { Notice(ErrorText(ex)); }
        finally { Busy(false); }
        _verification.Focus(FocusState.Programmatic);
    }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_busy) return;
        if (!ValidEmail()) { Notice("请输入有效邮箱。"); return; }
        if (!SupabaseAuthClient.IsEmailCode(_verification.Text.Trim())) { Notice("请输入邮件中的 6 位数字验证码。"); return; }
        var deferral = args.GetDeferral();
        Busy(true);
        try
        {
            await _account.VerifyEmailCodeAsync(_email.Text, _verification.Text);
            args.Cancel = false;
        }
        catch (Exception ex) { Notice(ErrorText(ex)); }
        finally { Busy(false); deferral.Complete(); }
    }

    private static string ErrorText(Exception ex) => ex switch
    {
        System.Net.Http.HttpRequestException or TaskCanceledException => "网络连接未完成；若已收到验证码可直接填写，否则稍后重试。",
        System.IO.IOException or UnauthorizedAccessException => "无法安全保存登录状态，请检查磁盘空间和文件权限。",
        _ => ex.Message,
    };
}
