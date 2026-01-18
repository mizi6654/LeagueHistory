using System.Diagnostics;
using System.Runtime.InteropServices;

namespace League.Infrastructure
{
    /// <summary>
    /// 低级键盘全局钩子（Low Level Keyboard Hook）
    /// 可以捕获全局按键事件，包括游戏内、其他窗口
    /// </summary>
    public class LowLevelKeyboardHook : IDisposable
    {
        // Windows API 常量
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        // 虚拟键码
        private const int VK_CONTROL = 0x11;   // Ctrl
        private const int VK_SHIFT = 0x10;     // Shift
        private const int VK_MENU = 0x12;      // Alt

        // 委托定义
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        // 钩子句柄
        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _proc;

        // 事件
        public event EventHandler<System.Windows.Forms.KeyEventArgs>? OnKeyDown;
        public event EventHandler<System.Windows.Forms.KeyEventArgs>? OnKeyUp;

        public LowLevelKeyboardHook()
        {
            _proc = HookCallback;
        }

        public void Hook()
        {
            using var currentProcess = Process.GetCurrentProcess();
            using var currentModule = currentProcess.MainModule ?? throw new InvalidOperationException("MainModule is null");

            _hookId = SetWindowsHookEx(
                WH_KEYBOARD_LL,
                _proc,
                GetModuleHandle(currentModule.ModuleName),
                0);

            if (_hookId == IntPtr.Zero)
            {
                throw new Exception("安装低级键盘钩子失败: " + Marshal.GetLastWin32Error());
            }
        }

        public void Unhook()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                // 获取当前 Ctrl/Shift/Alt 按下状态（使用 GetAsyncKeyState）
                bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                // 组合键码（与 Forms.KeyEventArgs 一致）
                Keys modifiers = Keys.None;
                if (ctrlDown) modifiers |= Keys.Control;
                if (shiftDown) modifiers |= Keys.Shift;
                if (altDown) modifiers |= Keys.Alt;

                Keys keyData = key | modifiers;

                var args = new System.Windows.Forms.KeyEventArgs(keyData);

                if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                {
                    OnKeyDown?.Invoke(this, args);
                    if (args.Handled)
                    {
                        return (IntPtr)1; // 拦截事件（可选）
                    }
                }
                else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                {
                    OnKeyUp?.Invoke(this, args);
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Unhook();
            GC.SuppressFinalize(this);
        }

        ~LowLevelKeyboardHook()
        {
            Unhook();
        }

        // Win32 API
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}