using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using bilibili_music_player_windows.Controls;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Services;
using bilibili_music_player_windows.ViewModels;

namespace bilibili_music_player_windows.Pages;

public sealed partial class FavoritesPage : Page, IResidentTab
{
    public LibraryViewModel ViewModel { get; } = App.Current.GetService<LibraryViewModel>();
    private readonly SettingsViewModel _settings = App.Current.GetService<SettingsViewModel>();
    private bool _updatingNavigation;
    public FavoritesPage()
    {
        InitializeComponent();
        LibraryRows.AddHandler(KeyDownEvent, new KeyEventHandler(OnRowKeyDown), true);
        Loaded += (_, _) => UpdateTreeSelection();
        ViewModel.LibraryChanged += (_, _) => UpdateTreeSelection();
    }
    private void UpdateTreeSelection()
    {
        // TreeView.SelectedItem can access a released native container after an
        // ItemsSource reset. Bind selection on the realized item instead.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (XamlRoot is null) return;
            _updatingNavigation = true;
            try
            {
                void Select(System.Collections.Generic.IEnumerable<LibraryNavItem> items)
                {
                    foreach (var item in items)
                    { item.IsSelected = item.Id == ViewModel.CurrentId && item.Kind == ViewModel.CurrentKind; Select(item.Children); }
                }
                Select(ViewModel.Roots);
                var compact = ViewModel.NavigationChoices.FirstOrDefault(n => n.Id == ViewModel.CurrentId && n.Kind == ViewModel.CurrentKind);
                if (!ReferenceEquals(NavigationPicker.SelectedItem, compact)) NavigationPicker.SelectedItem = compact;
            }
            finally { _updatingNavigation = false; }
        });
    }
    private void OnNavigationPicked(object sender, SelectionChangedEventArgs e)
    { if (!_updatingNavigation && e.AddedItems.FirstOrDefault() is LibraryNavItem item) ViewModel.Open(item.Id, item.Kind); }
    public void SetTabActive(bool active) { }

    private void OnTreeInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    { if (args.InvokedItem is LibraryNavItem item) ViewModel.Open(item.Id, item.Kind); }

    private async void OnRowClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LibraryDisplayRow row) return;
        if (row.NodeId is { } node)
            ViewModel.Open(node, ViewModel.Document.Nodes.Single(n => n.Id == node).Kind);
        else if (_settings.QuickPlayGesture == 0) await ViewModel.RunAsync(() => ViewModel.QuickPlayAsync(row));
    }
    private async void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_settings.QuickPlayGesture != 1 || sender is not FrameworkElement { DataContext: LibraryDisplayRow row } || row.MediaId is null) return;
        if (FromRowButton(e.OriginalSource, (FrameworkElement)sender)) return;
        e.Handled = true;
        await ViewModel.RunAsync(() => ViewModel.QuickPlayAsync(row));
    }
    private async void OnRowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter || _settings.QuickPlayGesture != 1) return;
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not ListViewItem && !ReferenceEquals(source, LibraryRows))
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
        if (source is not ListViewItem { Content: LibraryDisplayRow { MediaId: not null } row } container) return;
        if (FromRowButton(e.OriginalSource, container)) return;
        e.Handled = true;
        await ViewModel.RunAsync(() => ViewModel.QuickPlayAsync(row));
    }
    private static bool FromRowButton(object originalSource, FrameworkElement row)
    {
        for (var source = originalSource as DependencyObject; source is not null && !ReferenceEquals(source, row);
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source))
            if (source is Button) return true;
        return false;
    }
    private async void OnPlayAllClick(object sender, RoutedEventArgs e) => await ViewModel.RunAsync(() => ViewModel.PlayAsync());
    private void OnAppendAllClick(object sender, RoutedEventArgs e) => ViewModel.Queue(false);
    private async void OnSyncClick(object sender, RoutedEventArgs e) => await ViewModel.Repository.SyncAsync();
    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        var kind = ((FrameworkElement)sender).Tag as string ?? "playlist";
        var name = await NameAsync("新建歌单", "");
        if (name is not null) await ViewModel.RunAsync(() => ViewModel.CreateAsync(kind, name));
    }
    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        var node = ViewModel.Document.Nodes.Single(n => n.Id == ViewModel.CurrentId);
        var name = await NameAsync("重命名", node.Name);
        if (name is not null) await ViewModel.RunAsync(() => ViewModel.RenameAsync(name));
    }
    private async void OnMoveClick(object sender, RoutedEventArgs e)
    {
        var target = await ChooseAsync("移动到歌单", ViewModel.Folders().ToArray());
        if (target is not null) await ViewModel.RunAsync(() => ViewModel.MoveAsync(target.Id));
    }
    private async void OnReorderNodeClick(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: string delta }) await ViewModel.RunAsync(() => ViewModel.ReorderNodeAsync(int.Parse(delta))); }
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync("删除“" + ViewModel.Heading + "”", "此歌单及所有子歌单将一起删除。“我喜欢”和其他歌单中的歌曲不受影响。"))
            await ViewModel.RunAsync(ViewModel.DeleteAsync);
    }
    private async void OnCopyPlaylistClick(object sender, RoutedEventArgs e)
    {
        var target = await ChooseAsync("复制歌单歌曲", ViewModel.Playlists().Where(c => c.Id != ViewModel.CurrentId).ToArray(), "复制当前歌曲，不跟随源歌单更新。");
        if (target?.Id is { } id) await ViewModel.RunAsync(() => ViewModel.CopyPlaylistAsync(id));
    }
    private async void OnAddTracksClick(object sender, RoutedEventArgs e)
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 360,
            ItemsSource = ViewModel.Document.Media, DisplayMemberPath = nameof(LibraryMedia.Title) };
        var dialog = NewDialog("从收藏库添加歌曲", list, "添加");
        dialog.IsPrimaryButtonEnabled = false;
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItems.Count > 0;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RunAsync(() => ViewModel.AddMediaAsync(list.SelectedItems.Cast<LibraryMedia>().Select(m => m.Id).ToArray()));
    }

    private void OnRowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LibraryDisplayRow row } element) return;
        var menu = new MenuFlyout();
        Add(menu, "下一首播放", () => { ViewModel.Queue(true, row); return Task.CompletedTask; });
        Add(menu, "加入队列末尾", () => { ViewModel.Queue(false, row); return Task.CompletedTask; });
        if (row.SourceId is { } source)
            Add(menu, "打开来源歌单", () => { ViewModel.Open(source, "playlist"); return Task.CompletedTask; });
        if (row.MediaId is { } media)
        {
            Add(menu, "添加到歌单", async () =>
            {
                var target = await ChooseAsync("添加到歌单", ViewModel.Playlists().ToArray());
                if (target?.Id is { } id) await ViewModel.RunAsync(() => ViewModel.Repository.EditAsync(g => g.AddMedia(id, new[] { media })));
            });
            var liked = ViewModel.Document.Likes.Any(l => l.MediaId == media);
            Add(menu, liked ? "取消喜欢" : "标记为喜欢", () => ViewModel.RunAsync(() => ViewModel.Repository.EditAsync(g => g.Like(media, !liked))));
            Add(menu, "从收藏库移除", async () =>
            {
                if (await ConfirmAsync("移除“" + row.Title + "”", "这会将该音源从“我喜欢”和所有歌单中移除，其他版本不受影响。"))
                    await ViewModel.RunAsync(() => ViewModel.Repository.EditAsync(g => g.RemoveMedia(media)));
            });
        }
        if (row.EntryId is { } entry)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            if (!ViewModel.ExpandChildren)
            {
                Add(menu, "向上移动", () => ViewModel.RunAsync(() => ViewModel.ReorderEntryAsync(entry, -1)));
                Add(menu, "向下移动", () => ViewModel.RunAsync(() => ViewModel.ReorderEntryAsync(entry, 1)));
            }
            Add(menu, "从此歌单移除", () => ViewModel.RunAsync(() => ViewModel.RemoveEntryAsync(entry)));
        }
        if (row.NodeId is { } child && !ViewModel.ExpandChildren)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            Add(menu, "向上移动", () => ViewModel.RunAsync(() => ViewModel.ReorderChildAsync(child, -1)));
            Add(menu, "向下移动", () => ViewModel.RunAsync(() => ViewModel.ReorderChildAsync(child, 1)));
        }
        menu.ShowAt(element);
    }

    private static void Add(MenuFlyout menu, string label, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = label };
        item.Click += async (_, _) => await action();
        menu.Items.Add(item);
    }
    private ContentDialog NewDialog(string title, object content, string primary) => new()
    { XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = primary,
        CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
    private async Task<string?> NameAsync(string title, string value)
    {
        var text = new TextBox { Header = "名称", Text = value, MaxLength = 120, MinWidth = 280 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(text, "LibraryNameInput");
        var dialog = NewDialog(title, text, "确定");
        dialog.PrimaryButtonClick += (_, args) => { if (string.IsNullOrWhiteSpace(text.Text)) { args.Cancel = true; text.Focus(FocusState.Programmatic); } };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? text.Text.Trim() : null;
    }
    private async Task<LibraryChoice?> ChooseAsync(string title, LibraryChoice[] choices, string? explanation = null)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 280 };
        if (explanation is not null) panel.Children.Add(new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        if (choices.Length == 0) { panel.Children.Add(new TextBlock { Text = "还没有可选项目，请先创建歌单。" }); await NewDialog(title, panel, "知道了").ShowAsync(); return null; }
        var list = new ComboBox { ItemsSource = choices, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(list);
        return await NewDialog(title, panel, "确定").ShowAsync() == ContentDialogResult.Primary ? list.SelectedItem as LibraryChoice : null;
    }
    private async Task<bool> ConfirmAsync(string title, string message) =>
        await NewDialog(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 }, "确定").ShowAsync() == ContentDialogResult.Primary;
}
