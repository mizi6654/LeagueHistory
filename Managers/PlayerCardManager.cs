using League.Controls;
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

        // 新增：UI 操作互斥锁，防止并发修改 tableLayoutPanel
        public readonly SemaphoreSlim _uiLock = new SemaphoreSlim(1, 1);

        // 缓存相关
        private readonly Dictionary<long, PlayerMatchInfo> _cachedPlayerMatchInfos = new();
        private readonly Dictionary<long, int> _currentChampBySummoner = new();
        private readonly Dictionary<long, int> _summonerToColMap = new();
        private readonly Dictionary<long, PlayerMatchInfo> playerMatchCache = new();
        private readonly Dictionary<(int row, int column), (long summonerId, int championId)> playerCache = new();
        private readonly ConcurrentDictionary<long, PlayerCardControl> _cardBySummonerId = new();
        private static readonly ConcurrentDictionary<string, Image> _imageCache = new();

        private DateTime _lastFillTime = DateTime.MinValue;

        public PlayerCardManager(FormMain form, MatchQueryProcessor matchQueryProcessor)
        {
            _form = form;
            _matchQueryProcessor = matchQueryProcessor;

            // 关键：反向告诉 MatchQueryProcessor 我是谁
            matchQueryProcessor.SetPlayerCardManager(this);
        }

        #region 卡片创建和更新
        /// <summary>
        /// 【最终优化版】创建/更新基础卡片
        /// - 同一玩家仅换英雄：只更新头像（最小化闪烁）
        /// - 换人或首次：完整重建
        /// - 增加更严格的判断，减少不必要的重建
        /// </summary>
        public async Task CreateBasicCardsOnly(JArray team, bool isMyTeam, int row)
        {
            if (team == null || team.Count == 0) return;

            await _uiLock.WaitAsync();
            try
            {
                Debug.WriteLine($"[CreateBasicCardsOnly] Row={row} 开始处理，共 {team.Count} 人");

                int col = 0;
                foreach (var p in team)
                {
                    long summonerId = p["summonerId"]?.Value<long>() ?? 0;
                    int championId = p["championId"]?.Value<int>() ?? 0;

                    // 更新映射（即使是隐藏玩家也更新）
                    if (summonerId != 0)
                    {
                        _currentChampBySummoner[summonerId] = championId;
                        _summonerToColMap[summonerId] = col;
                    }

                    // 隐藏玩家特殊处理
                    if (summonerId == 0)
                    {
                        col++;
                        continue;
                    }

                    // === 获取当前位置现有卡片 ===
                    var existingPanel = _form.tableLayoutPanel1.GetControlFromPosition(col, row) as BorderPanel;
                    var existingCard = existingPanel?.Controls.OfType<PlayerCardControl>().FirstOrDefault();

                    // === 核心优化：同一玩家仅换英雄，只更新头像 ===
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
                        }
                        col++;
                        continue;
                    }

                    // === 需要重建的情况（换人、新增、首次加载）===
                    Debug.WriteLine($"[重建卡片] Row={row} Col={col} summonerId={summonerId} (换人或首次)");

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

                Debug.WriteLine($"[CreateBasicCardsOnly] Row={row} 处理完成");
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

        
        /// <summary>
        /// 填充玩家战绩信息 - 最终优化版（已加入缓存填充）
        /// </summary>
        public async Task FillPlayerMatchInfoAsync(JArray team, bool isMyTeam, int row)
        {
            if (team == null || team.Count == 0) return;

            // === 防抖：短时间内不再重复查询战绩 ===
            if ((DateTime.Now - _lastFillTime).TotalMilliseconds < 1200)
            {
                Debug.WriteLine($"[FillPlayerMatchInfoAsync] Row={row} 防抖跳过");
                return;
            }
            _lastFillTime = DateTime.Now;

            await _uiLock.WaitAsync();
            try
            {
                Debug.WriteLine($"[FillPlayerMatchInfoAsync] Row={row} 开始填充战绩");

                var fetchedInfos = await RunWithLimitedConcurrency(
                    team,
                    async p =>
                    {
                        long sid = p["summonerId"]?.Value<long>() ?? 0;
                        if (sid == 0)
                        {
                            return CreateHiddenPlayerInfo(0, p["championId"]?.Value<int>() ?? 0);
                        }

                        return await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(p);
                    },
                    maxConcurrency: 3);

                // 🔥🔥🔥 关键修复：把查询结果存入缓存 🔥🔥🔥
                int cachedCount = 0;
                lock (_cachedPlayerMatchInfos)
                {
                    foreach (var info in fetchedInfos)
                    {
                        if (info?.Player?.SummonerId > 0)
                        {
                            _cachedPlayerMatchInfos[info.Player.SummonerId] = info;
                            cachedCount++;
                        }
                    }
                }
                Debug.WriteLine($"[FillPlayerMatchInfoAsync] 已缓存 {cachedCount} 条玩家战绩数据");

                // 更新卡片UI
                int col = 0;
                foreach (var info in fetchedInfos)
                {
                    if (info?.Player != null)
                    {
                        CreateLoadingPlayerMatch(info, isMyTeam, row, col);
                    }
                    col++;
                }

                // 组队检测 & 颜色更新
                try
                {
                    var detector = new PartyDetector();
                    detector.Detect(fetchedInfos.Where(f => f != null).ToList());

                    foreach (var info in fetchedInfos)
                    {
                        if (info?.Player != null)
                            UpdatePlayerNameColor(info.Player.SummonerId, info.Player.NameColor);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FillPlayerMatchInfoAsync] 组队检测异常: {ex.Message}");
                }

                Debug.WriteLine($"[FillPlayerMatchInfoAsync] Row={row} 填充完成");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FillPlayerMatchInfoAsync] Row={row} 异常: {ex.Message}");
            }
            finally
            {
                _uiLock.Release();
            }
        }
        

        private PlayerMatchInfo CreateHiddenPlayerInfo(long summonerId, int championId)
        {
            string championName = Globals.resLoading.GetChampionById(championId)?.Name ?? "未知英雄";
            Image championIcon = Task.Run(() => Globals.resLoading.GetChampionIconAsync(championId)).Result
                                 ?? LoadErrorImage();

            return new PlayerMatchInfo
            {
                Player = new PlayerInfo
                {
                    SummonerId = summonerId,
                    ChampionId = championId,
                    ChampionName = championName,
                    Avatar = championIcon,
                    GameName = "隐藏玩家",
                    SoloRank = "隐藏",
                    FlexRank = "隐藏",
                    IsPublic = "隐藏",
                    NameColor = Color.Gray
                },
                MatchItems = new List<ListViewItem>(),
                HeroIcons = new ImageList()
            };
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
        /// 轻量级补全方法 - 仅用于兜底修复（不再作为主要逻辑）
        /// </summary>
        public async Task ValidateAndCompleteAllCards(JArray teamOne, JArray teamTwo)
        {
            if (teamOne == null || teamTwo == null) return;

            try
            {
                await _uiLock.WaitAsync();   // 使用同一把锁，避免和主流程冲突
                try
                {
                    Debug.WriteLine("[Validate] 开始轻量级补全检查");

                    var cardsNeedFix = GetCardsNeedCompletion();  // 你现有的方法

                    if (cardsNeedFix.Count == 0)
                    {
                        //Debug.WriteLine("[Validate] 所有卡片状态正常");
                        return;
                    }

                    Debug.WriteLine($"[Validate] 发现 {cardsNeedFix.Count} 张异常卡片，需要修复");

                    foreach (var cardInfo in cardsNeedFix)
                    {
                        // 隐藏玩家特殊处理
                        if (cardInfo.SummonerId == 0)
                        {
                            FixHiddenPlayerCard(cardInfo.Card);
                            continue;
                        }

                        // 查找对应玩家数据
                        var playerData = FindPlayerDataInSession(teamOne, teamTwo, cardInfo.SummonerId);
                        if (playerData == null) continue;

                        // 重新查询并更新
                        var matchInfo = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(playerData);
                        if (matchInfo?.Player != null)
                        {
                            UpdateCardUI(cardInfo.Card, matchInfo);
                        }

                        await Task.Delay(150); // 防止请求过猛
                    }
                }
                finally
                {
                    _uiLock.Release();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ValidateAndCompleteAllCards] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 专门修复隐藏玩家卡片显示
        /// </summary>
        private void FixHiddenPlayerCard(PlayerCardControl card)
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

        /// <summary>
        /// 统一更新卡片UI
        /// </summary>
        private void UpdateCardUI(PlayerCardControl card, PlayerMatchInfo matchInfo)
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

                // 更新战绩列表
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
            });
        }
        #endregion

        #region 缓存管理

        /// <summary>
        /// 供 ChatMessageBuilder 使用，返回当前所有玩家的战绩缓存
        /// </summary>
        public Dictionary<long, PlayerMatchInfo> GetAllCachedPlayerInfos()
        {
            lock (_cachedPlayerMatchInfos)
            {
                return new Dictionary<long, PlayerMatchInfo>(_cachedPlayerMatchInfos);
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