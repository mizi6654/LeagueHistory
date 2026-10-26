using League.Clients;
using League.Controls;
using League.Models;
using League.UIState;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Managers
{
    /// <summary>
    /// 管理比赛详情显示和回放功能
    /// </summary>
    public class MatchDetailManager
    {
        private readonly FormMain _form;
        private ToolTip? _toolTip;

        public MatchDetailManager(FormMain form)
        {
            _form = form;
        }

        #region 比赛详情显示
        /// <summary>
        /// 显示比赛详情
        /// </summary>
        public void ShowMatchDetails(MatchInfo matchInfo)
        {
            try
            {
                Debug.WriteLine("显示比赛详情: " + matchInfo.SelfPlayer?.GameName + " - " + matchInfo.GameTime);

                if (!ValidateMatchData(matchInfo))
                    return;

                var detailForm = CreateDetailForm(matchInfo);
                InitializeDetailForm(detailForm, matchInfo);
                detailForm.ShowDialog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("显示详情失败: " + ex.Message + "\n" + ex.StackTrace);
                MessageBox.Show("显示详情失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 验证比赛数据
        /// </summary>
        private bool ValidateMatchData(MatchInfo matchInfo)
        {
            if (matchInfo?.AllParticipants == null || matchInfo.AllParticipants.Count == 0)
            {
                MessageBox.Show("比赛数据不完整，无法显示详情");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 创建详情窗体
        /// </summary>
        private Form CreateDetailForm(MatchInfo matchInfo)
        {
            string championName = matchInfo.ChampionName ?? "未知英雄";
            string gameTime = matchInfo.GameTime ?? "未知时间";

            return new Form
            {
                Text = $"比赛详情 - {championName} - {gameTime}",
                Size = new Size(1300, 800),
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.White,
                MinimizeBox = false,
                MaximizeBox = true,
                ShowIcon = true,
                ShowInTaskbar = true
            };
        }

        /// <summary>
        /// 初始化详情窗体
        /// </summary>
        private void InitializeDetailForm(Form detailForm, MatchInfo matchInfo)
        {
            _toolTip = new ToolTip
            {
                AutoPopDelay = 30000,
                InitialDelay = 100,
                ReshowDelay = 50,
                ShowAlways = true,
                UseAnimation = true,
                UseFading = true
            };

            var layoutManager = new DetailedViewLayoutManager(_toolTip);
            var tabControl = CreateTabControl();

            // 修正：先创建TabPage再添加到TabControl
            TabPage detailedViewTab = new TabPage("详细视图")
            {
                BackColor = Color.White,
                Padding = new Padding(5)
            };

            tabControl.TabPages.Add(detailedViewTab);

            // 创建内容面板
            Panel panel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.White,
                Padding = new Padding(10)
            };

            CreateDetailedPlayerViews(panel, matchInfo, layoutManager);
            detailedViewTab.Controls.Add(panel);

            detailForm.Controls.Add(tabControl);

            detailForm.FormClosed += (s, e) => CleanupDetailForm(detailForm);
        }

        /// <summary>
        /// 创建Tab控件
        /// </summary>
        private TabControl CreateTabControl()
        {
            return new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 9f),
                ItemSize = new Size(80, 25),
                Padding = new Point(10, 5)
            };
        }

        /// <summary>
        /// 清理详情窗体资源
        /// </summary>
        private void CleanupDetailForm(Form detailForm)
        {
            try
            {
                _toolTip?.RemoveAll();
                _toolTip?.Dispose();
                DisposeImagesInControls(detailForm.Controls);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("释放资源时出错: " + ex.Message);
            }
        }
        #endregion

        #region 玩家视图创建
        /// <summary>
        /// 创建详细玩家视图
        /// </summary>
        private async void CreateDetailedPlayerViews(Panel panel, MatchInfo matchInfo, DetailedViewLayoutManager layoutManager)
        {
            if (matchInfo.AllParticipants == null) return;

            var blueTeam = matchInfo.AllParticipants
                .Where(p => p?["teamId"]?.Value<int>() == 100)
                .ToList();

            var redTeam = matchInfo.AllParticipants
                .Where(p => p?["teamId"]?.Value<int>() == 200)
                .ToList();

            int y = 10;

            // 蓝队标题
            AddTeamLabel(panel, "\ud83d\udd35 蓝队 (100)", Color.DodgerBlue, ref y);
            y += 30;

            // 蓝队玩家
            foreach (var participant in blueTeam)
            {
                if (participant == null) continue;

                var playerPanel = await layoutManager.CreateDetailedPlayerPanel(participant, matchInfo, 20, y, 100);
                if (playerPanel != null)
                {
                    panel.Controls.Add(playerPanel);
                    y += playerPanel.Height + 10;
                }
            }

            y += 20;

            // 红队标题
            AddTeamLabel(panel, "\ud83d\udd34 红队 (200)", Color.Red, ref y);
            y += 30;

            // 红队玩家
            foreach (var participant in redTeam)
            {
                if (participant == null) continue;

                var playerPanel = await layoutManager.CreateDetailedPlayerPanel(participant, matchInfo, 20, y, 200);
                if (playerPanel != null)
                {
                    panel.Controls.Add(playerPanel);
                    y += playerPanel.Height + 10;
                }
            }
        }

        /// <summary>
        /// 添加队伍标签
        /// </summary>
        private void AddTeamLabel(Panel panel, string text, Color color, ref int y)
        {
            var label = new Label
            {
                Text = text,
                Location = new Point(10, y),
                Size = new Size(200, 25),
                Font = new Font("微软雅黑", 11f, FontStyle.Bold),
                ForeColor = color
            };
            panel.Controls.Add(label);
            y += 30;
        }
        #endregion

        #region 回放功能
        /// <summary>
        /// 处理回放点击事件
        /// </summary>
        public async Task HandleReplayClick(MatchInfo matchInfo, MatchListPanel matchPanel)
        {
            try
            {
                if (!IsReplaySupported(matchInfo.QueueId))
                {
                    // 仅此一种必要提示可保留，也可改成静默 return
                    MessageBox.Show("该模式不支持查看回放！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Question);
                    return;
                }

                if (!matchInfo.IsReplayDownloaded)
                {
                    await DownloadAndPlayReplay(matchInfo, matchPanel);
                }
                else
                {
                    await PlayExistingReplay(matchInfo);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[回放] 处理异常: {ex}");
                // 不再弹错误框，避免多余提示
            }
        }

        /// <summary>
        /// 下载并播放回放（只弹一次确认框）
        /// </summary>
        private async Task DownloadAndPlayReplay(MatchInfo matchInfo, MatchListPanel matchPanel)
        {
            // ========== 唯一弹窗 ==========
            var result = MessageBox.Show(
                "确定下载并观看这场回放吗？\n\n" +
                $"比赛ID：{matchInfo.GameId}\n" +
                $"模式：{matchInfo.Mode}\n\n" +
                "注意：确定后会立即下载并播放，且只有当前版本的比赛可以下载，版本更新后旧回放会失效。",
                "查看回放",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            if (result != DialogResult.OK)
                return;

            Debug.WriteLine($"[回放] 确认下载 GameId={matchInfo.GameId}");
            Debug.WriteLine($"[回放] _statusBar is null? {_form._statusBar == null}");

            Cursor previousCursor = Cursor.Current;
            try
            {
                Cursor.Current = Cursors.WaitCursor;

                // 开始下载 —— 不自动清空，等结果再改
                _form._statusBar?.TipReplayDownloading();

                var downloadResult = await Globals.lcuClient.DownloadReplayAsync(matchInfo.GameId);

                switch (downloadResult)
                {
                    case ReplayDownloadResult.Success:
                    case ReplayDownloadResult.AlreadyExists:
                        matchInfo.IsReplayDownloaded = true;
                        matchPanel.Invalidate();

                        // 下载成功
                        _form._statusBar?.TipReplayDone();
                        // 下载成功直接进回放，不再弹成功提示
                        await Globals.lcuClient.PlayReplayAsync(matchInfo.GameId);
                        break;

                    case ReplayDownloadResult.Incompatible:
                        matchInfo.IsReplayExpired = true;
                        matchPanel.SetReplayStatus(matchInfo, "回放已过期");

                        // 下载失败
                        _form._statusBar?.ShowTip("回放已过期/版本不兼容");
                        Debug.WriteLine($"[回放] GameId {matchInfo.GameId} 版本不兼容，回放已过期");
                        break;

                    case ReplayDownloadResult.Timeout:
                        _form._statusBar?.ShowTip("下载超时");
                        break;
                    case ReplayDownloadResult.Failed:
                    case ReplayDownloadResult.RequestError:
                    default:
                        Debug.WriteLine($"[回放] GameId {matchInfo.GameId} 下载失败: {downloadResult}");
                        // 可选：UpdateReplayButtonStatus(matchPanel, matchInfo, "下载失败");
                        _form._statusBar?.ShowTip("下载失败");
                        break;
                }
            }
            finally
            {
                Cursor.Current = previousCursor;
            }
        }

        /// <summary>
        /// 播放已存在的回放（无弹窗）
        /// </summary>
        private async Task PlayExistingReplay(MatchInfo matchInfo)
        {
            Cursor previousCursor = Cursor.Current;
            try
            {
                Cursor.Current = Cursors.WaitCursor;

                // 开始进入回放
                _form._statusBar?.ShowTip("正在打开回放...", 0);
                await Globals.lcuClient.PlayReplayAsync(matchInfo.GameId);
                _form._statusBar?.ShowTip("回放已打开");   // 或 ClearTip()
            }
            finally
            {
                Cursor.Current = previousCursor;
            }
        }

        private bool IsReplaySupported(string queueTag)
        {
            return !string.IsNullOrEmpty(queueTag);
        }
        #endregion

        #region 工具方法
        /// <summary>
        /// 释放控件中的图片资源
        /// </summary>
        private void DisposeImagesInControls(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                if (control is PictureBox pictureBox && pictureBox.Image != null)
                {
                    try
                    {
                        pictureBox.Image.Dispose();
                        pictureBox.Image = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("释放图片资源失败: " + ex.Message);
                    }
                }
                if (control.HasChildren)
                {
                    DisposeImagesInControls(control.Controls);
                }
            }
        }

        /// <summary>
        /// 检查是否支持显示详细对局信息
        /// 只允许标准 5v5（固定10人）的峡谷/大乱斗系模式
        /// </summary>
        public bool IsSummonersRiftOrAram(string queueTag)
        {
            if (string.IsNullOrEmpty(queueTag)) return false;

            string[] allowed = new[]
            {
                // 排位
                "q_420", "q_440",

                // 匹配
                "q_400", "q_430",

                // 大乱斗 & 无限
                "q_450", "q_900", "q_1900",

                // 快速模式
                "q_480",

                // 海克斯乱斗
                "q_2400", "q_2450", "q_3270",

                // 人机（完整）
                "q_830", "q_840", "q_850",
                "q_870", "q_880", "q_890",

                // 末日人机（已确认是5v5峡谷）
                "q_950", "q_960",

                // 其他标准5v5
                "q_1020",  // 克隆大作战
                "q_1300",  // 极限闪击
                "q_1400",  // 终极魔典

                // 经典 / 训练
                "q_4310", "q_4320",
                "q_3140", "q_3100"
            };

            return allowed.Contains(queueTag);
        }
        #endregion
    }
}