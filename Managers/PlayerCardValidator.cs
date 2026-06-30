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
                        // 【关键修复】加强 null 判断
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

                        bool isLoading = name.Contains("加载中") || name.Contains("查询中") || name == "未知";
                        bool isFailed = name.Contains("失败") || soloRank.Contains("失败") || flexRank.Contains("失败");
                        bool isHidden = name.Contains("隐藏") || card.CurrentSummonerId == 0;

                        bool needsFix = (listCount == 0 || isLoading || isFailed) && !isHidden;

                        if (needsFix)
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
                                RetryCount = 0
                            });
                        }
                    }
                }
            });

            return result;
        }
    }
}