using System.Diagnostics;
using System.Runtime.InteropServices;

namespace League.Services
{
    public class GameInputService
    {
        #region Win32 P/Invoke 定义 (保持不变)
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const ushort VK_RETURN = 0x0D;
        private const ushort SCAN_RETURN = 0x1C;

        private readonly Random _random = new Random();
        #endregion

        /// <summary>
        /// 游戏内秒级战绩发送（原子级瞬发版：全队发送仅需 3-4 秒）
        /// </summary>
        public async Task<bool> SendInGameMessageAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            try
            {
                string[] lines = message.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    // 1. 唤起聊天框 (压缩回车时间)
                    SendHardwareKey(VK_RETURN, SCAN_RETURN, false);
                    await Task.Delay(_random.Next(15, 25));
                    SendHardwareKey(VK_RETURN, SCAN_RETURN, true);

                    // 预留极短的 UI 响应时间（100ms 足够游戏内 UI 聚焦）
                    await Task.Delay(_random.Next(80, 120));

                    // 2. 核心优化：整行文本作为一个原子操作瞬间注入 (耗时 0ms)
                    SendUnicodeStringInstant(line);

                    // 预留极其短暂的字数缓冲区反应时间
                    await Task.Delay(_random.Next(100, 150));

                    // 3. 回车发送
                    SendHardwareKey(VK_RETURN, SCAN_RETURN, false);
                    await Task.Delay(_random.Next(15, 25));
                    SendHardwareKey(VK_RETURN, SCAN_RETURN, true);

                    // 行间延迟（防国服速度过快被屏蔽，留 500ms 足够安全）
                    await Task.Delay(_random.Next(500, 700));
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[秒级输入失败]: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将整行字符串打包进同一个输入队列数组，实现不讲道理的瞬间粘贴效果
        /// </summary>
        private void SendUnicodeStringInstant(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            int len = text.Length;
            // 每个字符包含 按下 和 弹起 2个事件
            INPUT[] inputs = new INPUT[len * 2];
            int inputSize = Marshal.SizeOf(typeof(INPUT));

            for (int i = 0; i < len; i++)
            {
                char c = text[i];

                // 填充按下事件
                inputs[i * 2] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = c,
                            dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYDOWN,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                // 填充弹起事件
                inputs[i * 2 + 1] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = c,
                            dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };
            }

            // 一条系统指令将整个数组全部发送出去，底层多线程原子化执行
            SendInput((uint)inputs.Length, inputs, inputSize);
        }

        private void SendHardwareKey(ushort vk, ushort scan, bool isKeyUp)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = scan,
                        dwFlags = isKeyUp ? KEYEVENTF_KEYUP : KEYEVENTF_KEYDOWN,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}