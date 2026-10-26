using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using Windows.System;
using bilibili_music_player_windows.Services;

namespace bilibili_music_player_windows;

public sealed partial class MainWindow
{
    private TrayIcon? _tray;
    private bool _isInBackground;
    private bool _isExiting;

    private void InitializeWindowLifetime()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        AppWindow.SetIcon(iconPath);
        _tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), iconPath, ShowFromBackground, ExitApplication);
        AppWindow.Closing += OnAppWindowClosing;

        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            if (!TryGoBack()) HideToBackground();
            args.Handled = true;
        };
        ((Microsoft.UI.Xaml.FrameworkElement)Content).KeyboardAccelerators.Add(escape);
    }

    private bool TryGoBack()
    {
        if (!TitleBackButton.IsEnabled) return false;
        _searchViewModel.GoBack();
        return true;
    }

    private void HideToBackground()
    {
        if (_isInBackground || _isExiting) return;
        // A missing notification area must not strand an invisible application.
        if (_tray?.IsAvailable != true)
        {
            (AppWindow.Presenter as OverlappedPresenter)?.Minimize();
            return;
        }
        (_tabs?.ActivePage as IResidentTab)?.SetTabActive(false);
        _isInBackground = true;
        AppWindow.Hide();
    }

    internal void ShowFromBackground()
    {
        if (_isExiting) return;
        if (_isInBackground)
        {
            _isInBackground = false;
            (_tabs?.ActivePage as IResidentTab)?.SetTabActive(true);
        }
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting) return;
        args.Cancel = true;
        HideToBackground();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        // Exit after the native tray callback has returned, then release its subclass.
        DispatcherQueue.TryEnqueue(Close);
    }
}
