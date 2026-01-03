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
                    ShowReplayNotSupportedMessage();
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
                ShowReplayError(ex.Message);
            }
        }

        /// <summary>
        /// 下载并播放回放
        /// </summary>
        private async Task DownloadAndPlayReplay(MatchInfo matchInfo, MatchListPanel matchPanel)
        {
            var result = MessageBox.Show(
                $"点击确定将自动下载回放文件并开始播放比赛！\n比赛ID: {matchInfo.GameId}\n模式: {matchInfo.Mode}",
                "查看回放", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

            if (result == DialogResult.OK)
            {
                if (await Globals.lcuClient.DownloadReplayAsync(matchInfo.GameId))
                {
                    matchInfo.IsReplayDownloaded = true;
                    matchPanel.Invalidate();

                    if (!await Globals.lcuClient.PlayReplayAsync(matchInfo.GameId))
                    {
                        ShowReplayPlayError();
                    }
                }
                else
                {
                    ShowDownloadReplayError();
                }
            }
        }

        /// <summary>
        /// 播放已存在的回放
        /// </summary>
        private async Task PlayExistingReplay(MatchInfo matchInfo)
        {
            if (await Globals.lcuClient.PlayReplayAsync(matchInfo.GameId))
            {
                MessageBox.Show("回放启动成功！", "播放成功", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
            }
            else
            {
                MessageBox.Show("回放启动失败", "播放失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        /// <summary>
        /// 检查是否支持回放
        /// </summary>
        private bool IsReplaySupported(string queueTag)
        {
            if (string.IsNullOrEmpty(queueTag)) return false;

            string[] replayAllowed = new string[15]
            {
                "q_400", "q_420", "q_430", "q_440", "q_450", "q_480", "q_830", "q_840", "q_850", "q_900", "q_1010",
                "q_1020", "q_1900", "q_2400","q_3270"
            };
            return replayAllowed.Contains(queueTag);
        }

        /// <summary>
        /// 显示回放不支持消息
        /// </summary>
        private void ShowReplayNotSupportedMessage()
        {
            MessageBox.Show("该模式不支持查看回放！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Question);
        }

        /// <summary>
        /// 显示回放错误
        /// </summary>
        private void ShowReplayError(string message)
        {
            MessageBox.Show("处理回放失败: " + message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Hand);
        }

        /// <summary>
        /// 显示回放播放错误
        /// </summary>
        private void ShowReplayPlayError()
        {
            MessageBox.Show("回放下载完成，但启动播放失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        }

        /// <summary>
        /// 显示回放下载错误
        /// </summary>
        private void ShowDownloadReplayError()
        {
            MessageBox.Show("下载失败，请检查是否为最近比赛或支持回放的模式。",
                "下载失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
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
        /// 检查是否是召唤师峡谷或大乱斗模式
        /// </summary>
        public bool IsSummonersRiftOrAram(string queueTag)
        {
            if (string.IsNullOrEmpty(queueTag)) return false;

            string[] allowed = new string[15]
            {
                "q_400", "q_420", "q_430", "q_440", "q_450", "q_480", "q_830", "q_840", "q_850", "q_900", "q_1010",
                "q_1020", "q_1900", "q_2400","q_3270"
            };
            return allowed.Contains(queueTag);
        }
        #endregion
    }
}