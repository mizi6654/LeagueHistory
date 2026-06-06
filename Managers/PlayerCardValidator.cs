using League.Controls;
using League.Models;
using League.UIState;
using System.ComponentModel.DataAnnotations;

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

            // 加载中 / 查询中 / 失败状态
            if (playerName.Contains("加载中") || playerName.Contains("查询中") ||
                playerName == "查询失败" || playerName == "失败" ||
                soloRank.Contains("加载中") || soloRank == "失败" ||
                privacy.Contains("查询中") || privacy == "[失败]")
                return true;

            // 【核心】列表为空但不是隐藏玩家 → 需要补全
            if (listCount == 0 && !playerName.Contains("隐藏"))
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


        public List<PlayerCardValidationInfo> ForceGetAllCardsForCompletion()
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
                            if (card != null && !card.IsDisposed)
                            {
                                bool needsFix = false;
                                string name = card.lblPlayerName.Text?.Trim() ?? "";
                                string rank = card.lblSoloRank.Text?.Trim() ?? "";
                                int listCount = card.ListViewControl?.Items.Count ?? 0;

                                // 核心条件：列表为空 或者 仍在 loading 状态
                                if (listCount == 0 ||
                                    name.Contains("加载中") || name.Contains("查询中") ||
                                    rank.Contains("加载中") || rank == "未知" || rank == "失败")
                                {
                                    needsFix = true;
                                }

                                // 隐藏玩家特殊处理
                                if (name.Contains("隐藏") || card.CurrentSummonerId == 0)
                                {
                                    if (listCount == 0)
                                        //_validator.FixHiddenPlayerCard(card);  // 注意：这里要注入或直接调用
                                    continue;
                                }

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
                }
            });

            return result;
        }
    }
}