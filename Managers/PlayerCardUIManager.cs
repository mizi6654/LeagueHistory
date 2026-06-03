using League.Controls;
using League.Models;
using League.UIState;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Managers
{
    public class PlayerCardUIManager
    {
        private readonly FormMain _form;
        public readonly SemaphoreSlim _uiLock = new SemaphoreSlim(1, 1);

        public PlayerCardUIManager(FormMain form)
        {
            _form = form;
        }

        public async Task CreateBasicCardsOnly(JArray team, bool isMyTeam, int row,PlayerCardFactory factory, PlayerCardCache cache)
        {
            if (team == null || team.Count == 0) return;

            await _uiLock.WaitAsync();
            try
            {
                int col = 0;
                foreach (var p in team)
                {
                    long summonerId = p["summonerId"]?.Value<long>() ?? 0;
                    int championId = p["championId"]?.Value<int>() ?? 0;
                    string puuid = p["puuid"]?.ToString() ?? "";

                    // 更新缓存映射（隐藏玩家也需要更新 championId 映射）
                    cache?.UpdateCurrentChampion(summonerId, championId, col);

                    // === 获取当前位置现有卡片 ===
                    var existingPanel = _form.tableLayoutPanel1.GetControlFromPosition(col, row) as BorderPanel;
                    var existingCard = existingPanel?.Controls.OfType<PlayerCardControl>().FirstOrDefault();

                    // 同一玩家仅换英雄：只更新头像
                    if (existingCard != null && existingCard.CurrentSummonerId == summonerId)
                    {
                        if (existingCard.CurrentChampionId != championId)
                        {
                            Debug.WriteLine($"[仅换英雄] Row={row} Col={col} summonerId={summonerId} 更新头像");
                            var newAvatar = await Globals.resLoading.GetChampionIconAsync(championId);
                            FormUiStateManager.SafeInvoke(existingCard, () =>
                            {
                                if (!existingCard.IsDisposed)
                                {
                                    existingCard.SetAvatarOnly(newAvatar);
                                    existingCard.CurrentChampionId = championId;
                                }
                            });
                            existingCard.CurrentPuuId = puuid;
                        }
                        col++;
                        continue;
                    }

                    // 需要重建卡片
                    PlayerMatchInfo loadingInfo;

                    if (summonerId == 0)
                    {
                        loadingInfo = factory.CreateHiddenPlayerInfo(summonerId, championId);
                    }
                    else
                    {
                        loadingInfo = factory.CreateLoadingPlayerInfo(p);
                    }

                    CreateLoadingPlayerMatch(loadingInfo, isMyTeam, row, col,puuid);
                    col++;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CreateBasicCardsOnly] Row={row} 异常: {ex.Message}");
            }
            finally
            {
                _uiLock.Release();
            }
        }

        public void CreateLoadingPlayerMatch(PlayerMatchInfo matchInfo, bool isMyTeam, int row, int column,string puuid = "")
        {
            var player = matchInfo.Player;
            Color borderColor = row == 0 ? Color.Red : row == 1 ? Color.Blue : Color.Gray;

            var panel = new BorderPanel
            {
                BorderColor = borderColor,
                BorderWidth = 1,
                Padding = new Padding(2),
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };

            var card = new PlayerCardControl { Dock = DockStyle.Fill, Margin = new Padding(0) };
            card.Tag = player.SummonerId;

            string name = player.GameName ?? "未知";
            string soloRank = string.IsNullOrEmpty(player.SoloRank) ? "未知" : player.SoloRank;
            string flexRank = string.IsNullOrEmpty(player.FlexRank) ? "未知" : player.FlexRank;

            // 关键调用
            card.SetPlayerInfo(name, soloRank, flexRank, player.Avatar, player.IsPublic,
                matchInfo.MatchItems, player.NameColor, player.SummonerId, player.ChampionId, puuid ?? player.Puuid ?? "");

            // 【修复点】必须设置 SmallImageList
            if (matchInfo.HeroIcons != null)
            {
                card.ListViewControl.SmallImageList = matchInfo.HeroIcons;
            }

            card.ListViewControl.View = View.Details;

            panel.Controls.Add(card);

            // 注册到缓存
            _form._playerCardManager?.RegisterCard(player.SummonerId, card);   // 注意：这里需要 FormMain 里有 _playerCardManager

            FormUiStateManager.SafeInvoke(_form.tableLayoutPanel1, () =>
            {
                var oldControl = _form.tableLayoutPanel1.GetControlFromPosition(column, row);
                if (oldControl != null)
                {
                    _form.tableLayoutPanel1.Controls.Remove(oldControl);
                    oldControl.Dispose();
                }
                _form.tableLayoutPanel1.Controls.Add(panel, column, row);
            });
        }

        public void UpdateCardUI(PlayerCardControl card, PlayerMatchInfo matchInfo)
        {
            if (card == null || card.IsDisposed || matchInfo?.Player == null) return;

            FormUiStateManager.SafeInvoke(card, () =>
            {
                var p = matchInfo.Player;
                card.lblPlayerName.Text = p.GameName ?? "未知玩家";
                card.lblSoloRank.Text = p.SoloRank ?? "未知";
                card.lblFlexRank.Text = p.FlexRank ?? "未知";
                card.lblPrivacyStatus.Text = p.IsPublic ?? "隐藏";

                if (p.Avatar != null)
                    card.picHero.Image = p.Avatar;

                // 【重要修复】战绩列表 + 英雄头像
                if (matchInfo.MatchItems?.Count > 0)
                {
                    card.ListViewControl.Items.Clear();
                    foreach (var item in matchInfo.MatchItems)
                        card.ListViewControl.Items.Add(item);

                    if (matchInfo.HeroIcons != null)
                        card.ListViewControl.SmallImageList = matchInfo.HeroIcons;
                }

                if (p.NameColor != default)
                {
                    card.lblPlayerName.LinkColor = p.NameColor;
                    card.lblPlayerName.VisitedLinkColor = p.NameColor;
                    card.lblPlayerName.ActiveLinkColor = p.NameColor;
                }
                card.CurrentPuuId = matchInfo.Player.Puuid ?? "";
            });
        }

        public void UpdatePlayerNameColor(long summonerId, Color color, PlayerCardCache cache)
        {
            if (color == default(Color) || summonerId <= 0)
                return;

            if (cache.TryGetCard(summonerId, out var card) && card != null && !card.IsDisposed)
            {
                // 更安全的 Invoke 方式
                try
                {
                    if (card.InvokeRequired)
                    {
                        card.Invoke((MethodInvoker)(() =>
                        {
                            ApplyNameColor(card, color);
                        }));
                    }
                    else
                    {
                        ApplyNameColor(card, color);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
                {
                    Debug.WriteLine($"[UpdatePlayerNameColor] 控件尚未准备好或已释放: summonerId={summonerId}");
                    // 不抛出异常，静默处理
                }
            }
        }

        private void ApplyNameColor(PlayerCardControl card, Color color)
        {
            if (card == null || card.IsDisposed) return;

            card.lblPlayerName.LinkColor = color;
            card.lblPlayerName.VisitedLinkColor = color;
            card.lblPlayerName.ActiveLinkColor = color;
        }
    }
}