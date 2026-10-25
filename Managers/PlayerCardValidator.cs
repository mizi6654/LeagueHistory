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

        public void FixHiddenPlayerCard(PlayerCardControl card)
        {
            if (card == null || card.IsDisposed) return;

            FormUiStateManager.SafeInvoke(card, () =>
            {
                card.lblPlayerName.Text = "隐藏玩家";
                card.lblSoloRank.Text = "隐藏";
                card.lblFlexRank.Text = "隐藏";
                card.lblPrivacyStatus.Text = "隐藏";
                card.SetTopMasteries(null);
                card.SetSummonerLevel(0);
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
                        if (panel == null || panel.Controls.Count == 0)
                            continue;

                        var card = panel.Controls[0] as PlayerCardControl;
                        if (card == null || card.IsDisposed)
                            continue;

                        string name = card.lblPlayerName.Text?.Trim() ?? "";
                        string soloRank = card.lblSoloRank.Text?.Trim() ?? "";
                        string flexRank = card.lblFlexRank?.Text?.Trim() ?? "";
                        int listCount = card.ListViewControl?.Items.Count ?? 0;

                        // 加载中 / 查询中：第一次查询还在跑，不进补全
                        if (name.Contains("加载中") || name.Contains("查询中"))
                            continue;

                        // 真正隐藏（无身份）：不进补全列表，后面单独 FixHidden
                        bool isTrueHidden = name.Contains("隐藏") &&
                            card.CurrentSummonerId == 0 &&
                            string.IsNullOrEmpty(card.CurrentPuuId);
                        if (isTrueHidden)
                            continue;

                        bool isUnknownName =
                            name.Contains("未知") ||
                            name == "未知玩家" ||
                            string.IsNullOrWhiteSpace(name);

                        bool isFailedName =
                            name.Contains("失败") ||
                            soloRank.Contains("失败") ||
                            flexRank.Contains("失败");

                        // 显示成「隐藏」但其实有 SID/PUUID → 当完整补全处理
                        bool isFakeHidden = name.Contains("隐藏") &&
                            (card.CurrentSummonerId != 0 || !string.IsNullOrEmpty(card.CurrentPuuId));

                        bool hasMatches = listCount > 0;

                        // 1) 未知/失败 + 已有战绩 → 只补名字
                        if ((isUnknownName || isFailedName) && hasMatches)
                        {
                            result.Add(new PlayerCardValidationInfo
                            {
                                SummonerId = card.CurrentSummonerId,
                                ChampionId = card.CurrentChampionId,
                                Puuid = card.CurrentPuuId ?? "",
                                Row = row,
                                Column = col,
                                Card = card,
                                CurrentName = name,
                                CurrentSoloRank = soloRank,
                                CurrentFlexRank = flexRank,
                                RetryCount = 0,
                                NameOnly = true
                            });
                            continue;
                        }

                        // 2) 未知/失败/假隐藏 + 无战绩 → 完整补全
                        if ((isUnknownName || isFailedName || isFakeHidden) && !hasMatches)
                        {
                            result.Add(new PlayerCardValidationInfo
                            {
                                SummonerId = card.CurrentSummonerId,
                                ChampionId = card.CurrentChampionId,
                                Puuid = card.CurrentPuuId ?? "",
                                Row = row,
                                Column = col,
                                Card = card,
                                CurrentName = name,
                                CurrentSoloRank = soloRank,
                                CurrentFlexRank = flexRank,
                                RetryCount = 0,
                                NameOnly = false
                            });
                        }
                    }
                }
            });

            return result;
        }
    }
}