using League.Controls;
using League.Models;
using League.UIState;

namespace League.Managers
{
    public class PlayerCardValidator
    {
        private readonly FormMain _form;

        public PlayerCardValidator(FormMain form)
        {
            _form = form;
        }

        public List<PlayerCardValidationInfo> GetCardsNeedCompletion()
        {
            var result = new List<PlayerCardValidationInfo>();

            FormUiStateManager.SafeInvoke(_form.tableLayoutPanel1, () =>
            {
                for (int row = 0; row < _form.tableLayoutPanel1.RowCount; row++)
                {
                    for (int col = 0; col < _form.tableLayoutPanel1.ColumnCount; col++)
                    {
                        var panel = _form.tableLayoutPanel1.GetControlFromPosition(col, row) as BorderPanel;
                        if (panel?.Controls.Count > 0)
                        {
                            var card = panel.Controls[0] as PlayerCardControl;
                            if (card != null && !card.IsDisposed && CheckCardNeedsCompletion(card))
                            {
                                result.Add(new PlayerCardValidationInfo
                                {
                                    SummonerId = card.CurrentSummonerId,
                                    ChampionId = card.CurrentChampionId,
                                    Puuid = card.CurrentPuuId,
                                    Row = row,
                                    Column = col,
                                    Card = card,
                                    CurrentName = card.lblPlayerName.Text,
                                    CurrentSoloRank = card.lblSoloRank.Text,
                                    CurrentFlexRank = card.lblFlexRank.Text,
                                    HasAvatar = card.picHero.Image != null
                                });
                            }
                        }
                    }
                }
            });

            return result;
        }


        private bool CheckCardNeedsCompletion(PlayerCardControl card)
        {
            if (card == null || card.IsDisposed) return false;

            string playerName = card.lblPlayerName.Text?.Trim() ?? "";
            string soloRank = card.lblSoloRank.Text?.Trim() ?? "";
            string privacy = card.lblPrivacyStatus.Text?.Trim() ?? "";
            int listCount = card.ListViewControl?.Items.Count ?? 0;

            // ==================== 加强版条件 ====================

            // 明确的状态词
            if (playerName.Contains("加载中") ||
                playerName.Contains("查询中") ||
                playerName == "查询失败" ||
                playerName == "失败" ||
                soloRank.Contains("加载中") ||
                soloRank == "失败" ||
                privacy.Contains("查询中") ||
                privacy == "[失败]")
                return true;

            // 列表为空但不是隐藏玩家（核心条件）
            if (listCount == 0 && !playerName.Contains("隐藏"))
                return true;

            // 新增兜底：名字为空/未知 或 summonerId 为0 但没处理成隐藏
            if (string.IsNullOrWhiteSpace(playerName) ||
                playerName == "未知" ||
                (card.CurrentSummonerId == 0 && !playerName.Contains("隐藏")))
                return true;

            // 新增：排行榜为空 + 名字看起来是正常玩家
            if (listCount == 0 && playerName.Length > 1 && !playerName.Contains("隐藏"))
                return true;

            return false;
        }

        public void FixHiddenPlayerCard(PlayerCardControl card)
        {
            if (card == null || card.IsDisposed) return;

            FormUiStateManager.SafeInvoke(card, () =>
            {
                card.lblPlayerName.Text = "隐藏玩家";
                card.lblSoloRank.Text = "隐藏";
                card.lblFlexRank.Text = "隐藏";
                card.lblPrivacyStatus.Text = "隐藏";
                card.lblPlayerName.LinkColor = Color.Gray;
            });
        }

        // 同步更新UI，防止卡片显示不加载中
        public List<PlayerCardValidationInfo> ForceGetAllCardsForCompletion()
        {
            var result = new List<PlayerCardValidationInfo>();

            FormUiStateManager.SafeInvokeSync(_form.tableLayoutPanel1, () =>
            {
                for (int row = 0; row < _form.tableLayoutPanel1.RowCount; row++)
                {
                    for (int col = 0; col < _form.tableLayoutPanel1.ColumnCount; col++)
                    {
                        var panel = _form.tableLayoutPanel1.GetControlFromPosition(col, row) as BorderPanel;
                        if (panel?.Controls.Count > 0)
                        {
                            var card = panel.Controls[0] as PlayerCardControl;
                            if (card == null || card.IsDisposed) continue;

                            string name = card.lblPlayerName.Text?.Trim() ?? "";
                            string rank = card.lblSoloRank.Text?.Trim() ?? "";
                            int listCount = card.ListViewControl?.Items.Count ?? 0;

                            bool needsFix = listCount == 0 ||
                                            name.Contains("加载中") ||
                                            name.Contains("查询中") ||
                                            name == "未知" ||
                                            string.IsNullOrWhiteSpace(name) ||
                                            rank.Contains("加载中") ||
                                            rank == "失败" ||
                                            rank == "未知" ||
                                            (card.CurrentSummonerId == 0 && !name.Contains("隐藏"));

                            if (needsFix)
                            {
                                result.Add(new PlayerCardValidationInfo
                                {
                                    SummonerId = card.CurrentSummonerId,
                                    ChampionId = card.CurrentChampionId,
                                    Puuid = card.CurrentPuuId,
                                    Row = row,
                                    Column = col,
                                    Card = card,
                                    CurrentName = name
                                });
                            }
                        }
                    }
                }
            });

            return result;
        }
    }
}