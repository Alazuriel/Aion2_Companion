using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Threading;

namespace AION_2_Companion.Core.Map;

public sealed class GameMapInputService : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const uint WM_QUIT = 0x0012;

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEWHEEL = 0x020A;
    public event Action<int, int>? MouseClicked;
    private const int VK_M = 0x4D;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int id, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(
        out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(
        [MarshalAs(UnmanagedType.LPWStr)] string? name);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private readonly string _gameProcess;
    private readonly Func<bool> _mapIsOpen;
    private readonly Func<int, int, bool>? _shouldSuppressLeftClick;

    // Delegáltak referenciája, hogy a GC ne gyűjtse be a hookot
    private readonly HookProc _mouseCallback;

    private readonly DispatcherTimer _focusTimer;
    private readonly ManualResetEventSlim _hookReady = new(false);

    // Hook szál
    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _mouseHook;
    private bool _mDown;
    private bool _dragging;
    private bool _suppressLeftButton;
    private POINT _lastMouse;

    // UI szál
    private IntPtr _cachedWindow;
    private bool _cachedIsGame;
    private volatile bool _focused;
    private bool _disposed;

    public event Action? MapToggleRequested;
    public event Action<bool>? FocusChanged;
    public event Action<int, int>? MouseDragged;
    public event Action<int, int, int>? MouseWheel;

    public GameMapInputService(
        string gameProcess,
        Func<bool> mapIsOpen,
        Func<int, int, bool>? shouldSuppressLeftClick = null)
    {
        _gameProcess = gameProcess;
        _mapIsOpen = mapIsOpen;
        _shouldSuppressLeftClick = shouldSuppressLeftClick;

        _mouseCallback = MouseHook;

        _focusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _focusTimer.Tick += (_, _) => UpdateFocus();
    }
    
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GameMapInputService));

        if (_hookThread is not null)
            return;

        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "AION2 Map Input Hooks"
        };
        _hookThread.Start();

        // Megvárjuk, amíg a hookok fel vannak telepítve
        if (!_hookReady.Wait(2000))
        {
            Console.WriteLine("map",
                "[MAP INPUT ERROR] A hook szál nem indult el 2 másodpercen belül.");
        }

        UpdateFocus();
        _focusTimer.Start();

        Console.WriteLine("map", $"[MAP INPUT] Started | Game={_gameProcess}");
    }

    // Külön szál saját üzenetkezelő ciklussal.
    // Így a hookok nem függnek a UI szál terhelésétől.
    private void HookThreadMain()
    {
        _hookThreadId = GetCurrentThreadId();
        IntPtr module = GetModuleHandle(null);

        // A PostThreadMessage csak létrehozott üzenetsorhoz tud WM_QUIT-ot küldeni.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

        _mouseHook = SetWindowsHookEx(
            WH_MOUSE_LL, _mouseCallback, module, 0);

        _hookReady.Set();

        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_mouseHook != IntPtr.Zero)
            UnhookWindowsHookEx(_mouseHook);

        _mouseHook = IntPtr.Zero;
    }

    // UI szál: fókusz figyelése
    private void UpdateFocus()
    {
        if (_disposed)
            return;

        bool focused = IsGameForeground();

        if (focused == _focused)
            return;

        _focused = focused;

        FocusChanged?.Invoke(focused);
    }

    public bool IsGameForeground()
    {
        IntPtr hwnd = GetForegroundWindow();

        if (hwnd == IntPtr.Zero)
            return false;

        if (hwnd != _cachedWindow)
        {
            _cachedWindow = hwnd;
            _cachedIsGame = false;

            GetWindowThreadProcessId(hwnd, out uint pid);

            if (pid != 0)
            {
                try
                {
                    using var process = Process.GetProcessById((int)pid);

                    _cachedIsGame = string.Equals(
                        process.ProcessName,
                        _gameProcess,
                        StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex) when (
                    ex is ArgumentException
                    or InvalidOperationException
                    or Win32Exception)
                {
                    _cachedIsGame = false;
                }
            }
        }

        return _cachedIsGame
            && IsWindowVisible(hwnd)
            && !IsIconic(hwnd);
    }

    private static string GetForegroundProcessName()
    {
        IntPtr hwnd = GetForegroundWindow();

        if (hwnd == IntPtr.Zero)
            return "none";

        GetWindowThreadProcessId(hwnd, out uint pid);

        try
        {
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }

    // Hook szál
    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _focused && _mapIsOpen())
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            switch (wParam.ToInt32())
            {
                case WM_LBUTTONDOWN:
                    _suppressLeftButton = _shouldSuppressLeftClick?.Invoke(info.pt.X, info.pt.Y) == true;
                    _dragging = !_suppressLeftButton;
                    _lastMouse = info.pt;
                    int clickX = info.pt.X;
                    int clickY = info.pt.Y;
                    Dispatcher.UIThread.Post(() => MouseClicked?.Invoke(clickX, clickY));
                    if (_suppressLeftButton)
                        return (IntPtr)1;
                    break;

                case WM_LBUTTONUP:
                    _dragging = false;
                    if (_suppressLeftButton)
                    {
                        _suppressLeftButton = false;
                        return (IntPtr)1;
                    }
                    break;

                case WM_MOUSEMOVE when _dragging:
                    int dx = info.pt.X - _lastMouse.X;
                    int dy = info.pt.Y - _lastMouse.Y;
                    _lastMouse = info.pt;

                    if (dx != 0 || dy != 0)
                        Dispatcher.UIThread.Post(() =>
                            MouseDragged?.Invoke(dx, dy));
                    break;

                case WM_MOUSEWHEEL:
                    short delta = unchecked((short)(info.mouseData >> 16));

                    if (delta != 0)
                    {
                        int x = info.pt.X;
                        int y = info.pt.Y;
                        Dispatcher.UIThread.Post(() =>
                            MouseWheel?.Invoke(x, y, delta));
                    }
                    break;
            }
        }
        else
        {
            _dragging = false;
            _suppressLeftButton = false;
        }

        // Kattintás és görgetés továbbmegy a játéknak
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _focusTimer.Stop();

        // Leállítjuk a hook szál üzenetciklusát
        if (_hookThreadId != 0)
            PostThreadMessage(_hookThreadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);

        _hookThread?.Join(1000);
        _hookReady.Dispose();
    }
}
