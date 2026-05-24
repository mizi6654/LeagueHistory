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
            if (card == null || card.IsDisposed) return true;

            string playerName = card.lblPlayerName.Text;
            string soloRank = card.lblSoloRank.Text;
            string flexRank = card.lblFlexRank.Text;
            long summonerId = card.CurrentSummonerId;

            if (summonerId == 0)
                return playerName == "查询失败" || playerName == "失败";

            if (playerName == "查询失败" || playerName == "失败" ||
                playerName == "加载中..." || playerName.Contains("查询中"))
                return true;

            if (soloRank == "失败" || soloRank == "加载中..." ||
                flexRank == "失败" || flexRank == "加载中...")
                return true;

            if (card.ListViewControl.Items.Count == 0 && playerName != "隐藏玩家")
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
    }
}