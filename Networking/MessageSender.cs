using League.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace League.Networking
{
    /// <summary>
    /// 处理选人阶段的消息发送（热键监听和战绩信息发送）
    /// </summary>
    public class MessageSender
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(Keys vKey);

        #region 热键监听
        /// <summary>
        /// 监听热键并发送消息
        /// </summary>
        public void ListenAndSendMessageWhenHotkeyPressed(JArray myTeam, Action<JArray> sendAction)
        {
            Task.Run(() =>
            {
                Debug.WriteLine("[HotKey] 等待用户按下快捷键 F9...");

                bool keyPressed = false;
                while (!keyPressed)
                {
                    if ((GetAsyncKeyState(Keys.F9) & 0x8000) != 0)
                    {
                        Debug.WriteLine("[HotKey] 检测到 F9 被按下！");

                        // 切换到UI线程执行
                        if (Application.OpenForms.Count > 0)
                        {
                            Application.OpenForms[0]?.Invoke((MethodInvoker)(() =>
                            {
                                sendAction?.Invoke(myTeam);
                            }));
                        }
                        else
                        {
                            sendAction?.Invoke(myTeam);
                        }

                        // 防止连续触发
                        Thread.Sleep(500);
                        keyPressed = true;
                    }

                    Thread.Sleep(100);
                }
            });
        }
        #endregion

        #region 消息发送（可选，如果不使用可以删除）
        /// <summary>
        /// 发送选人阶段队伍摘要到聊天窗口
        /// </summary>
        public void SendChampSelectSummary(JArray myTeam, Dictionary<long, PlayerMatchInfo> cachedPlayerInfos, string currentPuuid)
        {
            var sb = new StringBuilder();

            foreach (var p in myTeam)
            {
                long summonerId = (long)p["summonerId"];
                if (!cachedPlayerInfos.TryGetValue(summonerId, out var info))
                {
                    continue;
                }

                // 获取当前的puuid
                string puuid = (string)p["puuid"];
                // 判断是否与窗口加载时的puuid一样，一样则是自己的，路过不发送
                if (!string.IsNullOrEmpty(puuid) && string.Equals(puuid, currentPuuid, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[跳过发送] 当前玩家:{p["gameName"].ToString()}");
                    continue;
                }

                string name = info.Player.GameName ?? "未知";
                string solo = info.Player.SoloRank ?? "未知";
                string flex = info.Player.FlexRank ?? "未知";

                var wins = info.WinHistory.Count(w => w);
                var total = info.WinHistory.Count;
                double winRate = total > 0 ? wins * 100.0 / total : 0;

                // 拼接近10场 KDA
                string kdaString = "";
                if (info.RecentMatches != null && info.RecentMatches.Count > 0)
                {
                    var last10 = info.RecentMatches.Take(10);
                    var kdaList = last10
                        .Select(m => $"{m.Kills}/{m.Deaths}/{m.Assists}");
                    kdaString = string.Join(" ", kdaList);
                }
                else
                {
                    kdaString = "无记录";
                }

                sb.AppendLine($"{name}: 单双排 {solo} | 灵活 {flex} | 近20场胜率: {winRate:F1}% | 近10场KDA: {kdaString}");
            }

            string allMessage = sb.ToString().Trim();
            var lines = allMessage.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                Clipboard.SetText(line);

                // 打开聊天框
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(50);

                // 粘贴
                SendKeys.SendWait("^v");
                Thread.Sleep(50);

                // 回车发送
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(100);
            }

            Debug.WriteLine("[战绩信息] SendKeys 发送完成 (逐行发送)");
        }
        #endregion
    }
}