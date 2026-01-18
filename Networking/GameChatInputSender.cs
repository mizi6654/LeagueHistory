using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class GameChatInputSender
{
    private static long _lastSendTick;

    #region Win32 Constants
    private const int INPUT_KEYBOARD = 1;
    private const int SW_RESTORE = 9;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint CF_UNICODETEXT = 13;
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const ushort SC_RETURN = 0x1C;
    private const ushort SC_CONTROL = 0x1D;
    private const ushort SC_V = 0x2F;
    #endregion

    #region Win32 Structs
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
    #endregion

    #region Win32 Imports
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();  // 新增：确认焦点
    #endregion

    #region Clipboard Imports (unchanged)
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);
    #endregion

    public static bool Send(string text)
    {
        long now = Environment.TickCount64;
        if (now - _lastSendTick < 1500)
        {
            Debug.WriteLine("[GameChat] 节流中，间隔不足");
            return false;
        }
        _lastSendTick = now;

        IntPtr hwnd = FindWindow("RiotWindowClass", null);
        if (hwnd == IntPtr.Zero)
        {
            Debug.WriteLine("[GameChat] 找不到游戏窗口");
            return false;
        }

        uint gameThread = GetWindowThreadProcessId(hwnd, out _);
        uint curThread = GetCurrentThreadId();
        bool attached = false;
        try
        {
            // 绑定线程，确保输入到游戏
            attached = AttachThreadInput(curThread, gameThread, true);
            Debug.WriteLine($"[GameChat] 线程绑定: {(attached ? "成功" : "失败")}");

            // 还原并激活窗口（多次尝试）
            ShowWindow(hwnd, SW_RESTORE);
            for (int i = 0; i < 3; i++)
            {
                SetForegroundWindow(hwnd);
                Thread.Sleep(100);
            }
            Thread.Sleep(200);  // 等待焦点稳定

            // 确认焦点（日志）
            IntPtr fgWnd = GetForegroundWindow();
            Debug.WriteLine($"[GameChat] 当前前台窗口: {fgWnd} (目标: {hwnd})");

            // ESC 清状态（防残留）
            PressKey(VK_ESCAPE, 0, true);  // down
            Thread.Sleep(20);
            PressKey(VK_ESCAPE, 0, false); // up
            Thread.Sleep(100);

            // 打开聊天框（双 Enter，确保进入输入模式）
            for (int i = 0; i < 2; i++)
            {
                PressKey(VK_RETURN, SC_RETURN, true);
                Thread.Sleep(30);
                PressKey(VK_RETURN, SC_RETURN, false);
                Thread.Sleep(300);  // 关键：UI 响应延迟
            }

            // 等待聊天框稳定
            Thread.Sleep(250);

            SetClipboardText(text);
            Thread.Sleep(100);

            // Ctrl + V 粘贴（混合键码）
            PressKey(VK_CONTROL, SC_CONTROL, true);
            Thread.Sleep(30);
            PressKey(VK_V, SC_V, true);
            Thread.Sleep(20);
            PressKey(VK_V, SC_V, false);
            Thread.Sleep(40);
            PressKey(VK_CONTROL, SC_CONTROL, false);
            Thread.Sleep(200);  // 关键：等待游戏解析文本

            // 发送 Enter
            Thread.Sleep(100);
            PressKey(VK_RETURN, SC_RETURN, true);
            Thread.Sleep(30);
            PressKey(VK_RETURN, SC_RETURN, false);

            Debug.WriteLine("[GameChat] ✅ 输入序列完成 (焦点确认后)");
            return true;
        }
        finally
        {
            if (attached) AttachThreadInput(curThread, gameThread, false);
        }
    }

    // 通用按键函数（VK + ScanCode 混合）
    private static void PressKey(ushort vk, ushort scan, bool down)
    {
        uint flags = down ? 0u : KEYEVENTF_KEYUP;
        if (scan != 0) flags |= KEYEVENTF_SCANCODE;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    // Clipboard (unchanged, but add error check)
    private static void SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
            IntPtr hMem = GlobalAlloc(0x0042, (UIntPtr)bytes.Length);
            if (hMem == IntPtr.Zero) return;
            IntPtr ptr = GlobalLock(hMem);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            GlobalUnlock(hMem);
            SetClipboardData(CF_UNICODETEXT, hMem);
            Debug.WriteLine($"[GameChat] 剪贴板设置: {text.Length} 字符");
        }
        finally
        {
            CloseClipboard();
        }
    }
}
