using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace bilibili_music_player_windows.Services;

/// <summary>Owns the notification icon and its native menu for the main window.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 73; // WM_APP + 73
    private const nuint SubclassId = 1;
    private readonly nint _window;
    private readonly Action _show;
    private readonly Action _exit;
    private readonly SubclassProc _callback;
    private readonly uint _taskbarCreated;
    private readonly nint _ownedIcon;
    private NotifyIconData _icon;
    private bool _disposed;

    public bool IsAvailable { get; private set; }

    public TrayIcon(nint window, string iconPath, Action show, Action exit)
    {
        _window = window;
        _show = show;
        _exit = exit;
        _callback = WindowProc; // Keep the unmanaged callback alive until Dispose.
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        if (!SetWindowSubclass(window, _callback, SubclassId, 0)) return;

        var dpi = GetDpiForWindow(window);
        _ownedIcon = LoadImage(0, iconPath, 1, GetSystemMetricsForDpi(49, dpi),
            GetSystemMetricsForDpi(50, dpi), 0x0010); // IMAGE_ICON / LR_LOADFROMFILE
        var icon = _ownedIcon;
        if (icon == 0) icon = SendMessage(window, 0x007F, 0, 0); // WM_GETICON / ICON_SMALL
        if (icon == 0) icon = SendMessage(window, 0x007F, 1, 0); // ICON_BIG
        if (icon == 0) icon = LoadIcon(0, (nint)32512); // Shared application icon
        _icon = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = window,
            Id = 1,
            Flags = 0x01 | 0x02 | 0x04 | 0x80, // MESSAGE | ICON | TIP | SHOWTIP
            CallbackMessage = CallbackMessage,
            Icon = icon,
            Tip = "BiliMusic Desktop",
            Info = "",
            InfoTitle = "",
        };
        AddIcon();
    }

    private void AddIcon()
    {
        IsAvailable = ShellNotifyIcon(0, ref _icon); // NIM_ADD
        if (!IsAvailable) return;
        _icon.Version = 4;
        if (!ShellNotifyIcon(4, ref _icon)) // NIM_SETVERSION
        {
            ShellNotifyIcon(2, ref _icon);
            IsAvailable = false;
        }
    }

    private nint WindowProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (message == _taskbarCreated && _taskbarCreated != 0)
                AddIcon();
            else if (message == CallbackMessage)
            {
                // Version 4 delivers the notification code in LOWORD(lParam).
                switch ((uint)(lParam.ToInt64() & 0xFFFF))
                {
                    case 0x0400: // NIN_SELECT
                    case 0x0401: // NIN_KEYSELECT
                        _show();
                        break;
                    case 0x007B: // WM_CONTEXTMENU, including keyboard invocation
                        ShowMenu();
                        break;
                }
                return 0;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Tray] {ex}"); }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            AppendMenu(menu, 0, 1, "显示窗口");
            AppendMenu(menu, 0x0800, 0, null); // MF_SEPARATOR
            AppendMenu(menu, 0, 2, "退出");
            GetCursorPos(out var point);
            SetForegroundWindow(_window);
            var command = TrackPopupMenuEx(menu, 0x0100 | 0x0080 | 0x0002,
                point.X, point.Y, _window, 0); // RETURNCMD | NONOTIFY | RIGHTBUTTON
            PostMessage(_window, 0, 0, 0); // Allow dismissal when clicking outside.
            if (command == 1) _show();
            else if (command == 2) _exit();
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsAvailable) ShellNotifyIcon(2, ref _icon); // NIM_DELETE
        IsAvailable = false;
        RemoveWindowSubclass(_window, _callback, SubclassId);
        if (_ownedIcon != 0) DestroyIcon(_ownedIcon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
