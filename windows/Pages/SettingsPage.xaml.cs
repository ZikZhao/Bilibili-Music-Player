using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using bilibili_music_player_windows.Controls;
using bilibili_music_player_windows.Services;
using bilibili_music_player_windows.ViewModels;

namespace bilibili_music_player_windows.Pages;

public sealed partial class SettingsPage : Page, IResidentTab
{
    public SettingsViewModel ViewModel { get; } = App.Current.GetService<SettingsViewModel>();
    public LibraryViewModel Library { get; } = App.Current.GetService<LibraryViewModel>();
    public SettingsPage() => InitializeComponent();
    public void SetTabActive(bool active) { if (active) _ = ViewModel.RefreshCacheSizeAsync(); }
    private async void OnSyncClick(object sender, RoutedEventArgs e) => await Library.Repository.SyncAsync();

    private async void OnAccountClick(object sender, RoutedEventArgs e)
    {
        if (!Library.IsSignedIn) { await new AccountDialog { XamlRoot = XamlRoot }.ShowAsync(); return; }
        var menu = new MenuFlyout();
        void Add(string title, Func<System.Threading.Tasks.Task> action)
        {
            var item = new MenuFlyoutItem { Text = title };
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }
        Add("重新登录", async () => await new AccountDialog { XamlRoot = XamlRoot }.ShowAsync());
        Add("导入本机收藏", () => Library.RunAsync(Library.Repository.ImportLocalAsync));
        Add("退出登录", async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "退出登录",
                Content = "账号的本机收藏和未同步修改会保留，退出后显示独立的本机收藏库。",
                PrimaryButtonText = "退出登录", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await Library.RunAsync(Library.Account.SignOutAsync);
        });
        menu.ShowAt((FrameworkElement)sender);
    }
}
