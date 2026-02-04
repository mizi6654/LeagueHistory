using League.Controls;
using League.Extensions;
using League.Models;
using League.Networking;
using League.Parsers;
using League.UIState;
using League.uitls;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using static League.FormMain;

namespace League.Managers
{
    /// <summary>
    /// 管理玩家卡片的创建、更新、缓存和UI显示
    /// </summary>
    public class PlayerCardManager
    {
        private readonly FormMain _form;
        private readonly MatchQueryProcessor _matchQueryProcessor;

        // 缓存相关
        private readonly Dictionary<long, PlayerMatchInfo> _cachedPlayerMatchInfos = new();
        private readonly Dictionary<long, int> _currentChampBySummoner = new();
        private readonly Dictionary<long, int> _summonerToColMap = new();
        private readonly Dictionary<long, PlayerMatchInfo> playerMatchCache = new();
        private readonly Dictionary<(int row, int column), (long summonerId, int championId)> playerCache = new();
        private readonly ConcurrentDictionary<long, PlayerCardControl> _cardBySummonerId = new();
        private static readonly ConcurrentDictionary<string, Image> _imageCache = new();
        public PlayerCardManager(FormMain form, MatchQueryProcessor matchQueryProcessor)
        {
            _form = form;
            _matchQueryProcessor = matchQueryProcessor;

            // 关键：反向告诉 MatchQueryProcessor 我是谁
            matchQueryProcessor.SetPlayerCardManager(this);
        }

        #region 卡片创建和更新
        /// <summary>
        /// 创建基础卡片（只显示头像和基本信息）
        /// </summary>
        public async Task CreateBasicCardsOnly(JArray team, bool isMyTeam, int row)
        {
            //Debug.WriteLine($"[CreateBasicCardsOnly] 开始铺底座卡片 - {(isMyTeam ? "我方" : "敌方")} Row={row}，共 {team.Count} 人");

            int col = 0;
            foreach (var p in team)
            {
                long summonerId = (long)p["summonerId"];
                int championId = (int)p["championId"];

                // 更新映射
                _currentChampBySummoner[summonerId] = championId;
                _summonerToColMap[summonerId] = col;
                var positionKey = (row, col);
                playerCache[positionKey] = (summonerId, championId);

                // 检查现有卡片
                var existingPanel = _form.tableLayoutPanel1.GetControlFromPosition(col, row) as BorderPanel;
                var existingCard = existingPanel?.Controls.Count > 0 ? existingPanel.Controls[0] as PlayerCardControl : null;

                if (existingCard != null && !existingCard.IsDisposed)
                {
                    long oldSummonerId = existingCard.CurrentSummonerId;

                    if (oldSummonerId == summonerId)
                    {
                        // 同一玩家，只更新头像
                        if (existingCard.CurrentChampionId != championId)
                        {
                            //Debug.WriteLine($"[换英雄优化] Row={row}, Col={col}, {summonerId} 从 {existingCard.CurrentChampionId} → {championId}，仅更新头像");
                            var newAvatar = await Globals.resLoading.GetChampionIconAsync(championId);
                            FormUiStateManager.SafeInvoke(existingCard, () =>
                            {
                                existingCard.SetAvatarOnly(newAvatar);
                                existingCard.CurrentChampionId = championId;
                            });
                        }
                        col++;
                        continue;
                    }
                    else
                    {
                        // 换人了，需要重建卡片
                        //Debug.WriteLine($"[换人重建] Row={row}, Col={col}, 从 {oldSummonerId} → {summonerId}，重建卡片");
                    }
                }

                // 创建新的"加载中"卡片
                string championName = Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown";
                Image avatar = await Globals.resLoading.GetChampionIconAsync(championId);

                var loadingInfo = new PlayerMatchInfo
                {
                    Player = new PlayerInfo
                    {
                        SummonerId = summonerId,
                        ChampionId = championId,
                        ChampionName = championName,
                        Avatar = avatar,
                        GameName = "加载中...",
                        SoloRank = "加载中...",
                        FlexRank = "加载中...",
                        IsPublic = "[查询中]"
                    },
                    MatchItems = new List<ListViewItem>(),
                    HeroIcons = new ImageList()
                };

                CreateLoadingPlayerMatch(loadingInfo, isMyTeam, row, col);
                col++;
            }
        }

        /// <summary>
        /// 填充玩家战绩信息
        /// </summary>
        public async Task FillPlayerMatchInfoAsync(JArray team, bool isMyTeam, int row)
        {
            //Debug.WriteLine($"[FillPlayerMatchInfoAsync] 开始异步战绩查询 {(isMyTeam ? "我方" : "敌方")}，行号: {row}");
            var fetchedInfos = await RunWithLimitedConcurrency(
                team,
                async p =>
                {
                    long sid = p["summonerId"]?.Value<long>() ?? 0;
                    int cid = p["championId"]?.Value<int>() ?? 0;
                    PlayerMatchInfo info;

                    // 先检查缓存
                    lock (_cachedPlayerMatchInfos)
                    {
                        if (_cachedPlayerMatchInfos.TryGetValue(sid, out info))
                        {
                            if (_currentChampBySummoner.TryGetValue(sid, out int current) && current == cid)
                            {
                                int col = _summonerToColMap.TryGetValue(sid, out int c) ? c : 0;
                                var card = GetPlayerCardAtPosition(row, col);
                                if (card != null && card.CurrentSummonerId == sid && !card.IsLoading)
                                {
                                    return info;
                                }
                                CreateLoadingPlayerMatch(info, isMyTeam, row, col);
                            }
                            return info;
                        }
                    }

                    // 非缓存命中，执行查询
                    info = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(p);
                    if (info == null)
                    {
                        Debug.WriteLine($"[跳过] summonerId={sid} 获取失败，info 为 null");
                        var failedInfo = CreateFailedPlayerInfo(sid, cid);
                        int col = _summonerToColMap.TryGetValue(sid, out int c2) ? c2 : 0;
                        CreateLoadingPlayerMatch(failedInfo, isMyTeam, row, col);
                        return null;
                    }

                    // 加入缓存
                    lock (_cachedPlayerMatchInfos)
                        _cachedPlayerMatchInfos[sid] = info;

                    // 确保玩家仍是当前英雄
                    if (_currentChampBySummoner.TryGetValue(sid, out int curCid) && curCid == cid)
                    {
                        int col = _summonerToColMap.TryGetValue(sid, out int c) ? c : 0;
                        CreateLoadingPlayerMatch(info, isMyTeam, row, col);
                    }
                    else
                    {
                        Debug.WriteLine($"[跳过战绩更新] summonerId={sid} 已更换英雄");
                    }

                    return info;
                },
                maxConcurrency: 3
            );

            // 分析组队关系
            var detector = new PartyDetector();
            detector.Detect(fetchedInfos.Where(f => f != null).ToList());

            // 更新颜色
            foreach (var info in fetchedInfos)
            {
                if (info?.Player == null) continue;
                UpdatePlayerNameColor(info.Player.SummonerId, info.Player.NameColor);
            }
        }

        /// <summary>
        /// 创建加载中的玩家卡片
        /// </summary>
        private void CreateLoadingPlayerMatch(PlayerMatchInfo matchInfo, bool isMyTeam, int row, int column)
        {
            var player = matchInfo.Player;
            var heroIcons = matchInfo.HeroIcons;
            var matchItems = matchInfo.MatchItems;

            Color borderColor = row == 0 ? Color.Red :
                                row == 1 ? Color.Blue : Color.Gray;

            var panel = new BorderPanel
            {
                BorderColor = borderColor,
                BorderWidth = 1,
                Padding = new Padding(2),
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };

            var card = new PlayerCardControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            // 注册映射
            card.Tag = player.SummonerId;
            _cardBySummonerId[matchInfo.Player.SummonerId] = card;

            string name = player.GameName ?? "未知";
            string soloRank = string.IsNullOrEmpty(player.SoloRank) ? "未知" : player.SoloRank;
            string flexRank = string.IsNullOrEmpty(player.FlexRank) ? "未知" : player.FlexRank;
            Color nameColor = matchInfo.Player.NameColor;

            card.SetPlayerInfo(name, soloRank, flexRank, player.Avatar, player.IsPublic,
                matchItems, nameColor, player.SummonerId, player.ChampionId);
            card.ListViewControl.SmallImageList = heroIcons;
            card.ListViewControl.View = View.Details;

            panel.Controls.Add(card);

            // 更新UI
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
        #endregion

        #region 卡片数据校验与补全
        /// <summary>
        /// 获取所有需要补全的卡片信息
        /// </summary>
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
                        if (panel != null && panel.Controls.Count > 0)
                        {
                            var card = panel.Controls[0] as PlayerCardControl;
                            if (card != null && !card.IsDisposed)
                            {
                                // 检查卡片是否需要补全
                                bool needsCompletion = CheckCardNeedsCompletion(card);

                                if (needsCompletion)
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
                }
            });

            Debug.WriteLine($"[GetCardsNeedCompletion] 找到 {result.Count} 张需要补全的卡片");
            return result;
        }

        /// <summary>
        /// 检查单个卡片是否需要补全
        /// </summary>
        private bool CheckCardNeedsCompletion(PlayerCardControl card)
        {
            if (card == null || card.IsDisposed) return true;

            string playerName = card.lblPlayerName.Text;
            string soloRank = card.lblSoloRank.Text;
            string flexRank = card.lblFlexRank.Text;
            bool hasAvatar = card.picHero.Image != null;
            long summonerId = card.CurrentSummonerId;

            // 对于summonerId=0的隐藏玩家特殊处理
            if (summonerId == 0)
            {
                // 隐藏玩家应该有特殊显示，而不是"查询失败"
                if (playerName == "查询失败" || playerName == "失败")
                {
                    Debug.WriteLine($"[检查隐藏玩家] summonerId=0 显示为失败，需要修正为隐藏玩家");
                    return true;
                }

                // 隐藏玩家可能有头像（皮肤头像），这没问题
                // 如果是隐藏玩家且显示正常，不需要补全
                if (playerName == "隐藏玩家" || playerName == "隐藏")
                {
                    return false;
                }
            }

            // 检查是否为查询失败
            if (playerName == "查询失败" || playerName == "失败")
            {
                Debug.WriteLine($"[检查卡片] summonerId={summonerId} 查询失败");
                return true;
            }

            // 检查是否为查询中
            if (playerName == "加载中..." || playerName.Contains("查询中"))
            {
                Debug.WriteLine($"[检查卡片] summonerId={summonerId} 查询中");
                return true;
            }

            // 检查是否缺少头像（除了隐藏玩家）
            if (!hasAvatar && summonerId != 0)
            {
                Debug.WriteLine($"[检查卡片] summonerId={summonerId} 缺少头像");
                return true;
            }

            // 检查段位信息
            if (soloRank == "失败" || soloRank == "加载中..." ||
                flexRank == "失败" || flexRank == "加载中...")
            {
                Debug.WriteLine($"[检查卡片] summonerId={summonerId} 段位信息异常");
                return true;
            }

            // 检查是否缺少战绩数据（隐藏玩家可以没有战绩）
            if (card.ListViewControl.Items.Count == 0 && summonerId != 0 && playerName != "隐藏玩家" && playerName != "隐藏")
            {
                Debug.WriteLine($"[检查卡片] summonerId={summonerId} 缺少战绩数据");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 重新获取并更新指定玩家的卡片数据
        /// </summary>
        public async Task<bool> RetryAndUpdatePlayerCard(PlayerCardValidationInfo cardInfo, JToken playerData)
        {
            try
            {
                if (cardInfo.Card == null || cardInfo.Card.IsDisposed || playerData == null)
                {
                    return false;
                }

                // 重新查询战绩信息
                var matchInfo = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(playerData);
                if (matchInfo == null || matchInfo.Player == null)
                {
                    Debug.WriteLine($"[重试更新] summonerId={cardInfo.SummonerId} 获取战绩信息失败");
                    return false;
                }

                // 避免重复创建，检查卡片是否已被更新
                FormUiStateManager.SafeInvoke(cardInfo.Card, () =>
                {
                    if (cardInfo.Card.IsDisposed) return;

                    // 检查卡片状态是否已改变（可能已被其他线程更新）
                    bool stillNeedsUpdate = CheckCardNeedsCompletion(cardInfo.Card);
                    if (!stillNeedsUpdate)
                    {
                        Debug.WriteLine($"[重试更新] summonerId={cardInfo.SummonerId} 卡片已更新，跳过");
                        return;
                    }

                    // 更新卡片信息
                    var player = matchInfo.Player;

                    // 更新头像（避免重复设置相同的图片）
                    if (player.Avatar != null && cardInfo.Card.picHero.Image != player.Avatar)
                    {
                        cardInfo.Card.picHero.Image?.Dispose();
                        cardInfo.Card.picHero.Image = player.Avatar;
                    }

                    // 更新文本信息
                    if (cardInfo.Card.lblPlayerName.Text != player.GameName)
                    {
                        cardInfo.Card.lblPlayerName.Text = player.GameName ?? "未知玩家";
                    }

                    if (cardInfo.Card.lblSoloRank.Text != player.SoloRank)
                    {
                        cardInfo.Card.lblSoloRank.Text = player.SoloRank ?? "未知";
                    }

                    if (cardInfo.Card.lblFlexRank.Text != player.FlexRank)
                    {
                        cardInfo.Card.lblFlexRank.Text = player.FlexRank ?? "未知";
                    }

                    if (cardInfo.Card.lblPrivacyStatus.Text != player.IsPublic)
                    {
                        cardInfo.Card.lblPrivacyStatus.Text = player.IsPublic ?? "隐藏";
                    }

                    // 更新战绩列表
                    if (matchInfo.MatchItems != null && matchInfo.MatchItems.Any())
                    {
                        cardInfo.Card.ListViewControl.Items.Clear();
                        foreach (var item in matchInfo.MatchItems)
                        {
                            cardInfo.Card.ListViewControl.Items.Add(item);
                        }

                        if (matchInfo.HeroIcons != null)
                        {
                            cardInfo.Card.ListViewControl.SmallImageList = matchInfo.HeroIcons;
                        }
                    }

                    // 更新名字颜色
                    if (player.NameColor != default(Color))
                    {
                        cardInfo.Card.lblPlayerName.LinkColor = player.NameColor;
                        cardInfo.Card.lblPlayerName.VisitedLinkColor = player.NameColor;
                        cardInfo.Card.lblPlayerName.ActiveLinkColor = player.NameColor;
                    }

                    Debug.WriteLine($"[重试更新] summonerId={cardInfo.SummonerId} 卡片更新成功");
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[重试更新异常] summonerId={cardInfo.SummonerId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从Session数据中查找玩家数据
        /// </summary>
        public JToken FindPlayerDataInSession(JArray teamOne, JArray teamTwo, long summonerId)
        {
            if (teamOne == null || teamTwo == null) return null;

            // 在teamOne中查找
            var player = teamOne.FirstOrDefault(p =>
                p["summonerId"]?.Value<long>() == summonerId);

            if (player == null)
            {
                // 在teamTwo中查找
                player = teamTwo.FirstOrDefault(p =>
                    p["summonerId"]?.Value<long>() == summonerId);
            }

            return player;
        }

        /// <summary>
        /// 批量校验并补全所有卡片
        /// </summary>
        public async Task ValidateAndCompleteAllCards(JArray teamOne, JArray teamTwo)
        {
            if (teamOne == null || teamTwo == null) return;

            try
            {
                Debug.WriteLine("[批量校验] 开始批量校验并补全卡片");

                // 获取所有需要补全的卡片
                var cardsNeedCompletion = GetCardsNeedCompletion();

                if (cardsNeedCompletion.Count == 0)
                {
                    Debug.WriteLine("[批量校验] 没有需要补全的卡片");
                    return;
                }

                Debug.WriteLine($"[批量校验] 需要补全 {cardsNeedCompletion.Count} 张卡片");

                // 处理每张需要补全的卡片
                foreach (var cardInfo in cardsNeedCompletion)
                {
                    try
                    {
                        // 特殊处理summonerId=0的隐藏玩家
                        if (cardInfo.SummonerId == 0)
                        {
                            Debug.WriteLine($"[隐藏玩家修复] 修复summonerId=0的卡片显示");
                            FormUiStateManager.SafeInvoke(cardInfo.Card, () =>
                            {
                                if (!cardInfo.Card.IsDisposed)
                                {
                                    // 修正隐藏玩家的显示
                                    if (cardInfo.CurrentName == "查询失败" || cardInfo.CurrentName == "失败")
                                    {
                                        cardInfo.Card.lblPlayerName.Text = "隐藏玩家";
                                    }
                                    if (cardInfo.CurrentSoloRank == "隐藏玩家" || cardInfo.CurrentSoloRank == "失败")
                                    {
                                        cardInfo.Card.lblSoloRank.Text = "隐藏";
                                    }
                                    if (cardInfo.CurrentFlexRank == "隐藏玩家" || cardInfo.CurrentFlexRank == "失败")
                                    {
                                        cardInfo.Card.lblFlexRank.Text = "隐藏";
                                    }
                                    cardInfo.Card.lblPrivacyStatus.Text = "隐藏";
                                    cardInfo.Card.lblPlayerName.LinkColor = Color.Gray;
                                }
                            });
                            continue;
                        }

                        // 普通玩家：重新查询数据
                        var playerData = FindPlayerDataInSession(teamOne, teamTwo, cardInfo.SummonerId);
                        if (playerData != null)
                        {
                            await RetryAndUpdatePlayerCard(cardInfo, playerData);
                        }
                        else
                        {
                            Debug.WriteLine($"[批量校验] 未找到summonerId={cardInfo.SummonerId}的玩家数据");
                        }

                        // 避免过快请求
                        await Task.Delay(200);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[批量校验卡片异常] summonerId={cardInfo.SummonerId}: {ex.Message}");
                    }
                }

                Debug.WriteLine("[批量校验] 批量校验完成");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[批量校验异常]: {ex.Message}");
            }
        }
        #endregion

        #region 缓存管理
        /// <summary>
        /// 供 FormMain 热键发送战绩时使用，安全获取缓存中的玩家战绩信息
        /// </summary>
        public bool TryGetCachedPlayerInfo(long summonerId, out PlayerMatchInfo info)
        {
            lock (_cachedPlayerMatchInfos)
            {
                return _cachedPlayerMatchInfos.TryGetValue(summonerId, out info);
            }
        }

        /// <summary>
        /// 清空所有缓存
        /// </summary>
        public void ClearAllCaches()
        {
            playerMatchCache.Clear();
            playerCache.Clear();
            _cachedPlayerMatchInfos.Clear();
            _currentChampBySummoner.Clear();
            _summonerToColMap.Clear();
            _cardBySummonerId.Clear();
        }

        /// <summary>
        /// 清空游戏状态
        /// </summary>
        public void ClearGameState()
        {
            _form.lastChampSelectSnapshot.Clear();
            _currentChampBySummoner.Clear();
            _summonerToColMap.Clear();
            _cachedPlayerMatchInfos.Clear();
            playerMatchCache.Clear();
            _cardBySummonerId.Clear();
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 限制并发数的任务执行
        /// </summary>
        public async Task<List<TResult>> RunWithLimitedConcurrency<TInput, TResult>(
            IEnumerable<TInput> inputs,
            Func<TInput, Task<TResult>> taskFunc,
            int maxConcurrency = 3)
        {
            var indexedInputs = inputs.Select((input, index) => new { input, index }).ToList();
            var results = new TResult[indexedInputs.Count];
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task>();

            foreach (var item in indexedInputs)
            {
                await semaphore.WaitAsync();

                var task = Task.Run(async () =>
                {
                    try
                    {
                        var result = await taskFunc(item.input);
                        results[item.index] = result;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[并发异常] Index {item.index}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            return results.ToList();
        }

        /// <summary>
        /// 创建失败时的玩家信息
        /// </summary>
        public PlayerMatchInfo CreateFailedPlayerInfo(long summonerId, int championId)
        {
            // 获取英雄信息
            string championName = GetChampionName(championId);
            Image championIcon = Task.Run(() => GetChampionIconAsync(championId)).Result ??
                                LoadErrorImage();

            // 如果是summonerId=0，直接返回隐藏玩家信息
            if (summonerId == 0)
            {
                return new PlayerMatchInfo
                {
                    Player = new PlayerInfo
                    {
                        SummonerId = summonerId,
                        ChampionId = championId,
                        ChampionName = GetChampionName(championId),
                        GameName = "隐藏玩家",
                        IsPublic = "隐藏",
                        SoloRank = "隐藏",
                        FlexRank = "隐藏",
                        Avatar = championIcon,
                        NameColor = Color.Gray
                    },
                    MatchItems = new List<ListViewItem>(),
                    HeroIcons = new ImageList()
                };
            }

            // 普通玩家查询失败
            return new PlayerMatchInfo
            {
                Player = new PlayerInfo
                {
                    SummonerId = summonerId,
                    ChampionId = championId,
                    ChampionName = "查询失败",
                    GameName = "失败",
                    IsPublic = "[失败]",
                    SoloRank = "失败",
                    FlexRank = "失败",
                    Avatar = championIcon,
                    NameColor = Color.DarkRed
                },
                MatchItems = new List<ListViewItem>(),
                HeroIcons = new ImageList()
            };
        }

        /// <summary>
        /// 获取英雄名称
        /// </summary>
        private string GetChampionName(int championId)
        {
            return Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown";
        }

        /// <summary>
        /// 获取英雄图标
        /// </summary>
        private async Task<Image> GetChampionIconAsync(int championId)
        {
            return await Globals.resLoading.GetChampionIconAsync(championId);
        }

        /// <summary>
        /// 加载错误图片
        /// </summary>
        private Image LoadErrorImage()
        {
            return Image.FromFile(AppDomain.CurrentDomain.BaseDirectory + "Assets\\Defaults\\Profile.png");
        }

        /// <summary>
        /// 获取指定位置的玩家卡片
        /// </summary>
        private PlayerCardControl GetPlayerCardAtPosition(int row, int column)
        {
            var panel = _form.tableLayoutPanel1.GetControlFromPosition(column, row) as BorderPanel;
            return panel?.Controls.Count > 0 ? panel.Controls[0] as PlayerCardControl : null;
        }

        /// <summary>
        /// 更新玩家名字颜色
        /// </summary>
        private void UpdatePlayerNameColor(long summonerId, Color color)
        {
            if (_cardBySummonerId.TryGetValue(summonerId, out var card))
            {
                card.Invoke((MethodInvoker)(() =>
                {
                    card.lblPlayerName.LinkColor = color;
                    card.lblPlayerName.VisitedLinkColor = color;
                    card.lblPlayerName.ActiveLinkColor = color;
                }));
            }
        }
        #endregion
    }
}