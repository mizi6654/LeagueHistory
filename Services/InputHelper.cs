using System.Diagnostics;
using System.Runtime.InteropServices;

namespace League.Services
{
    public static class InputHelper
    {
        #region Win32 API

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
            public static int Size => Marshal.SizeOf(typeof(INPUT));
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
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

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        #endregion

        /// <summary>
        /// 确保 League of Legends 游戏窗口在前台
        /// </summary>
        public static bool BringGameToForeground()
        {
            var procs = Process.GetProcessesByName("League of Legends");
            if (procs.Length == 0)
                procs = Process.GetProcessesByName("LeagueClientUx");

            foreach (var p in procs)
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    SetForegroundWindow(p.MainWindowHandle);
                    Task.Delay(180).Wait();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 发送单个按键
        /// </summary>
        private static void SendKey(ushort vk, bool down)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = vk;
            inputs[0].u.ki.dwFlags = down ? 0u : KEYEVENTF_KEYUP;
            SendInput(1, inputs, INPUT.Size);
        }

        /// <summary>
        /// 发送字符串（支持中文）
        /// </summary>
        private static void SendText(string text)
        {
            foreach (char c in text)
            {
                ushort scan = c;
                INPUT[] inputs = new INPUT[2];

                // Down
                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].u.ki.wScan = scan;
                inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;

                // Up
                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].u.ki.wScan = scan;
                inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

                SendInput(2, inputs, INPUT.Size);
                Task.Delay(8).Wait(); // 字符间隔
            }
        }

        /// <summary>
        /// 核心：模拟游戏内发送一条聊天消息
        /// </summary>
        public static async Task<bool> SimulateChatSendAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            try
            {
                if (!BringGameToForeground())
                {
                    Debug.WriteLine("[InputHelper] 无法激活游戏窗口");
                    return false;
                }

                await Task.Delay(300); // 增加等待时间

                // 多次尝试按Enter打开聊天框
                SendKey(0x0D, true); await Task.Delay(50); SendKey(0x0D, false);
                await Task.Delay(120);

                SendText(message);
                await Task.Delay(100);

                SendKey(0x0D, true); await Task.Delay(50); SendKey(0x0D, false);

                Debug.WriteLine($"[InputHelper] 已模拟发送：{message.Length} 字符 → {message}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InputHelper] 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 多行消息逐行发送（推荐用于长战绩）
        /// </summary>
        public static async Task<bool> SimulateMultiLineChatAsync(string fullMessage, int delayBetweenLines = 800)
        {
            var lines = fullMessage.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                await SimulateChatSendAsync(trimmed);
                await Task.Delay(delayBetweenLines);   // 加大间隔
            }
            return true;
        }
    }
}