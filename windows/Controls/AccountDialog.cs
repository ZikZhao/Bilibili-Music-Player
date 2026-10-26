using System;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using bilibili_music_player_windows.Services;

namespace bilibili_music_player_windows.Controls;

/// <summary>Native email/password account UI, with no embedded web page or stored password.</summary>
public sealed class AccountDialog : ContentDialog
{
    private readonly SupabaseAccountService _account = App.Current.GetService<SupabaseAccountService>();
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "登录", "注册", "找回密码" }, SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _email = new() { Header = "邮箱", PlaceholderText = "name@example.com", MaxLength = 254 };
    private readonly PasswordBox _password = new() { Header = "密码" };
    private readonly PasswordBox _confirm = new() { Header = "确认密码" };
    private readonly TextBox _verification = new() { Header = "邮件验证码或原始重置链接", PlaceholderText = "粘贴邮件中的验证码或未打开的重置链接" };
    private readonly Button _send = new() { Content = "发送重置邮件" };
    private readonly InfoBar _notice = new() { IsOpen = false, Visibility = Visibility.Collapsed, IsClosable = false, Severity = InfoBarSeverity.Error, MaxWidth = 430 };
    private bool _busy;
    private bool _signupPending;

    public AccountDialog()
    {
        Title = "账户与云同步"; PrimaryButtonText = "登录"; CloseButtonText = "取消";
        DefaultButton = ContentDialogButton.Primary;
        _email.Text = _account.Session?.Email ?? "";
        var panel = new StackPanel { Spacing = 14, MinWidth = 300, MaxWidth = 440 };
        panel.Children.Add(new TextBlock { Text = "登录后同步收藏、歌单和文件夹。未登录时也可以在本机使用。", TextWrapping = TextWrapping.Wrap });
        foreach (var control in new UIElement[] { _mode, _email, _send, _verification, _password, _confirm, _notice }) panel.Children.Add(control);
        Content = panel;
        AutomationProperties.SetAutomationId(_mode, "AccountMode");
        AutomationProperties.SetAutomationId(_email, "AccountEmail");
        AutomationProperties.SetAutomationId(_password, "AccountPassword");
        AutomationProperties.SetAutomationId(_confirm, "AccountConfirmPassword");
        AutomationProperties.SetAutomationId(_verification, "AccountVerification");
        AutomationProperties.SetAutomationId(this, "AccountDialog");
        _mode.SelectionChanged += (_, _) => UpdateMode();
        _send.Click += async (_, _) => await SendRecoveryAsync();
        PrimaryButtonClick += OnPrimaryClick;
        Closed += (_, _) => { _password.Password = ""; _confirm.Password = ""; _verification.Text = ""; };
        Closing += (_, args) => { if (_busy) args.Cancel = true; };
        UpdateMode();
    }

    private void UpdateMode()
    {
        _signupPending = false;
        _verification.Text = "";
        _password.Password = _confirm.Password = "";
        _password.Visibility = Visibility.Visible;
        _verification.Header = "邮件验证码或原始重置链接";
        _verification.PlaceholderText = "粘贴邮件中的验证码或未打开的重置链接";
        var reset = _mode.SelectedIndex == 2;
        _send.Visibility = _verification.Visibility = reset ? Visibility.Visible : Visibility.Collapsed;
        _confirm.Visibility = _mode.SelectedIndex != 0 ? Visibility.Visible : Visibility.Collapsed;
        _password.Header = reset ? "新密码（至少 8 个字符）" : _mode.SelectedIndex == 1 ? "密码（至少 8 个字符）" : "密码";
        PrimaryButtonText = reset ? "重设密码" : _mode.SelectedIndex == 1 ? "注册" : "登录";
        _notice.IsOpen = false; _notice.Visibility = Visibility.Collapsed;
    }
    private bool ValidEmail() => MailAddress.TryCreate(_email.Text.Trim(), out var address) && address.Address == _email.Text.Trim();
    private void Notice(string text, bool success = false)
    { _notice.Message = text; _notice.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error; _notice.Visibility = Visibility.Visible; _notice.IsOpen = true; }
    private void Busy(bool value)
    {
        _busy = value; IsPrimaryButtonEnabled = !value; _mode.IsEnabled = _email.IsEnabled = _send.IsEnabled = !value;
        _password.IsEnabled = _confirm.IsEnabled = _verification.IsEnabled = !value;
        // A token response must not change accounts after a cancelled dialog.
        IsSecondaryButtonEnabled = !value;
    }
    private async Task SendRecoveryAsync()
    {
        if (_busy) return;
        if (!ValidEmail()) { Notice("请输入有效邮箱。"); return; }
        Busy(true);
        try { await _account.SendRecoveryAsync(_email.Text); Notice("如果该邮箱已注册，将收到重置邮件。请在下方填写邮件验证码，或复制邮件中尚未打开的重置链接。", true); }
        catch (Exception ex) { Notice(ErrorText(ex)); }
        finally { Busy(false); }
    }
    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_busy) return;
        if (!ValidEmail()) { Notice("请输入有效邮箱。"); return; }
        if (!_signupPending && string.IsNullOrEmpty(_password.Password)) { Notice("请输入密码。"); return; }
        if (!_signupPending && _mode.SelectedIndex != 0 && (_password.Password.Length < 8 || _password.Password != _confirm.Password))
        { Notice("密码至少需要 8 个字符，且两次输入应一致。"); return; }
        if (_signupPending && string.IsNullOrWhiteSpace(_verification.Text)) { Notice("请粘贴邮件中的确认信息。"); return; }
        var deferral = args.GetDeferral();
        Busy(true);
        try
        {
            if (_signupPending)
            {
                await _account.ConfirmEmailAsync(_email.Text, _verification.Text);
                args.Cancel = false;
            }
            else if (_mode.SelectedIndex == 0)
            {
                await _account.SignInAsync(_email.Text, _password.Password);
                args.Cancel = false;
            }
            else if (_mode.SelectedIndex == 1)
            {
                if (await _account.SignUpAsync(_email.Text, _password.Password)) args.Cancel = false;
                else
                {
                    _signupPending = true;
                    _password.Password = _confirm.Password = "";
                    _password.Visibility = _confirm.Visibility = Visibility.Collapsed;
                    _verification.Header = "注册邮件中的验证码或原始确认链接";
                    _verification.PlaceholderText = "粘贴确认邮件中的验证码或尚未打开的链接";
                    _verification.Visibility = Visibility.Visible;
                    PrimaryButtonText = "确认邮箱";
                    Notice("已提交注册。将确认邮件中的验证码或尚未打开的链接粘贴到这里即可完成确认；也可以点击邮件链接后返回登录。已注册的邮箱请直接登录。", true);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_verification.Text)) { Notice("请填写邮件中的验证信息。"); return; }
                await _account.ResetPasswordAsync(_email.Text, _verification.Text, _password.Password);
                _mode.SelectedIndex = 0;
                _password.Password = _confirm.Password = "";
                Notice("密码已更新，请使用新密码登录。", true);
            }
        }
        catch (Exception ex) { Notice(ErrorText(ex)); }
        finally { Busy(false); deferral.Complete(); }
    }
    private static string ErrorText(Exception ex) => ex switch
    {
        System.Net.Http.HttpRequestException or TaskCanceledException => "暂时无法连接，请稍后重试。",
        System.IO.IOException or UnauthorizedAccessException => "无法安全保存登录状态，请检查磁盘空间和文件权限。",
        _ => ex.Message,
    };
}
