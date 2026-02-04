using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace League.Services
{
    public static class InGameChatHelper
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        private const byte VK_RETURN = 0x0D;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        /// <summary>
        /// 模拟按下回车键
        /// </summary>
        private static void PressEnter()
        {
            keybd_event(VK_RETURN, 0, 0, 0);
            Thread.Sleep(10); // 必须有微小延迟，否则DX引擎无法识别
            keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, 0);
        }

        /// <summary>
        /// 模拟按下 Ctrl+V
        /// </summary>
        private static void PressCtrlV()
        {
            keybd_event(VK_CONTROL, 0, 0, 0);
            keybd_event(VK_V, 0, 0, 0);
            Thread.Sleep(10);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        }

        /// <summary>
        /// 在 STA 线程中设置剪贴板文本
        /// </summary>
        private static void SetClipboardText(string text)
        {
            Thread staThread = new Thread(() =>
            {
                try
                {
                    Clipboard.SetText(text);
                }
                catch { /* 忽略剪贴板占用导致的异常 */ }
            });
            staThread.SetApartmentState(ApartmentState.STA); // 剪贴板必须在STA线程
            staThread.Start();
            staThread.Join();
        }

        /// <summary>
        /// 游戏内发送多行消息（带防屏蔽延迟）
        /// </summary>
        public static async Task SendMessageAsync(string fullMessage)
        {
            // 按行拆分消息
            string[] lines = fullMessage.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                Debug.WriteLine($"[游戏内消息]：{line}");

                // 1. 将当前行文本放入剪贴板
                SetClipboardText(line);

                // 2. 模拟回车打开聊天框
                PressEnter();
                await Task.Delay(50); // 等待聊天框UI展开

                // 3. 模拟 Ctrl+V 粘贴
                PressCtrlV();
                await Task.Delay(50); // 等待文本粘贴进去

                // 4. 模拟回车发送消息
                PressEnter();

                // 5. 核心防御：延迟 400 毫秒，防止触发 Riot 刷屏禁言机制
                await Task.Delay(400);
            }
        }
    }
}