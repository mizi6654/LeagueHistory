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

        // 缓存相关
        public readonly Dictionary<long, PlayerMatchInfo> _cachedPlayerMatchInfos = new();
        private readonly Dictionary<long, int> _currentChampBySummoner = new();
        private readonly Dictionary<long, int> _summonerToColMap = new();
        private readonly Dictionary<(int row, int column), (long summonerId, int championId)> playerCache = new();
        private readonly ConcurrentDictionary<long, PlayerCardControl> _cardBySummonerId = new();

        // 新增：存储每个位置的基础信息
        private readonly Dictionary<(int row, int col), PlayerBasicInfo> _positionInfo = new();

        // 新增：记录已经查询过的玩家
        private readonly HashSet<long> _queriedPlayers = new();

        // 新增：完整队伍数据缓存
        private JArray _fullSessionData = null;

        public PlayerCardManager(FormMain form, MatchQueryProcessor matchQueryProcessor)
        {
            _form = form;
            _matchQueryProcessor = matchQueryProcessor;
            matchQueryProcessor.SetPlayerCardManager(this);
        }

        #region 1. 基础卡片创建

        /// <summary>
        /// 创建基础卡片（立即显示英雄头像）
        /// </summary>
        public async Task CreateBasicCardsOnly(JArray team, bool isMyTeam, int row)
        {
            //Debug.WriteLine($"[创建基础卡片] {(isMyTeam ? "我方" : "敌方")} 行:{row}, 人数:{team?.Count ?? 0}");

            // 确保在UI线程中执行
            await Task.Run(() => _form.Invoke(() =>
            {
                try
                {
                    if (team == null)
                    {
                        Debug.WriteLine("[创建基础卡片] team为null");
                        return;
                    }

                    for (int col = 0; col < team.Count; col++)
                    {
                        CreateSingleBasicCard(team[col], row, col, isMyTeam);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[创建基础卡片] UI线程异常: {ex.Message}");
                }
            }));
        }

        /// <summary>
        /// 创建基础卡片 - 记录玩家信息
        /// </summary>
        private void CreateSingleBasicCard(JToken player, int row, int col, bool isMyTeam)
        {
            try
            {
                long summonerId = player["summonerId"]?.Value<long>() ?? 0;
                long obfuscatedId = player["obfuscatedSummonerId"]?.Value<long>() ?? 0;
                int championId = player["championId"]?.Value<int>() ?? 0;
                string gameName = player["gameName"]?.ToString() ?? "";
                string visibility = player["nameVisibilityType"]?.ToString() ?? "UNHIDDEN";

                // 存储基础信息
                _positionInfo[(row, col)] = new PlayerBasicInfo
                {
                    SummonerId = summonerId,
                    ObfuscatedId = obfuscatedId,
                    ChampionId = championId,
                    GameName = gameName,
                    Visibility = visibility,
                    IsPlaceholder = false,
                    LastUpdated = DateTime.Now
                };

                // 记录已查询（对于非隐藏玩家）
                if (summonerId > 0 || obfuscatedId > 0)
                {
                    long actualId = summonerId > 0 ? summonerId : obfuscatedId;
                    _queriedPlayers.Add(actualId);
                }

                // 原有创建卡片逻辑...
                UpdateCacheMappings(summonerId, championId, row, col);
                string championName = Globals.resLoading.GetChampionById(championId)?.Name ?? "未知";
                var avatar = LoadChampionAvatar(championId);
                var loadingInfo = CreateLoadingPlayerInfo(summonerId, championId, championName, avatar, row);

                // 如果是隐藏玩家
                if (visibility == "HIDDEN" || summonerId == 0)
                {
                    loadingInfo.Player.GameName = "隐藏玩家";
                    loadingInfo.Player.IsPublic = "隐藏";
                    loadingInfo.Player.SoloRank = "隐藏";
                    loadingInfo.Player.FlexRank = "隐藏";
                }

                CreateOrUpdateCard(loadingInfo, row, col, isMyTeam);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[创建单个卡片] 位置({row},{col})异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新缓存映射
        /// </summary>
        private void UpdateCacheMappings(long summonerId, int championId, int row, int col)
        {
            _currentChampBySummoner[summonerId] = championId;
            _summonerToColMap[summonerId] = col;
            playerCache[(row, col)] = (summonerId, championId);
        }

        /// <summary>
        /// 创建加载中的玩家信息
        /// </summary>
        private PlayerMatchInfo CreateLoadingPlayerInfo(long summonerId, int championId,
            string championName, Image avatar, int row)
        {
            return new PlayerMatchInfo
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
                    IsPublic = "[查询中]",
                    NameColor = GetTeamColor(row) // 使用队伍颜色
                },
                MatchItems = new List<ListViewItem>(),
                HeroIcons = new ImageList()
            };
        }

        #endregion

        #region 2. 卡片UI操作

        /// <summary>
        /// 创建或更新卡片
        /// </summary>
        private void CreateOrUpdateCard(PlayerMatchInfo matchInfo, int row, int col, bool isMyTeam)
        {
            var existingPanel = _form.tableLayoutPanel1.GetControlFromPosition(col, row) as BorderPanel;

            // 检查是否已有相同玩家的卡片
            if (IsSamePlayerCard(existingPanel, matchInfo.Player.SummonerId))
            {
                UpdateExistingCard(existingPanel, matchInfo);
            }
            else
            {
                CreateNewCard(matchInfo, row, col, isMyTeam);
            }
        }

        /// <summary>
        /// 检查是否已有相同玩家的卡片
        /// </summary>
        private bool IsSamePlayerCard(BorderPanel? panel, long summonerId)
        {
            if (panel == null || panel.Controls.Count == 0) return false;

            var existingCard = panel.Controls[0] as PlayerCardControl;
            return existingCard != null && existingCard.CurrentSummonerId == summonerId;
        }

        /// <summary>
        /// 更新现有卡片
        /// </summary>
        private void UpdateExistingCard(BorderPanel panel, PlayerMatchInfo matchInfo)
        {
            var card = panel.Controls[0] as PlayerCardControl;
            if (card == null) return;

            // 更新头像
            if (card.CurrentChampionId != matchInfo.Player.ChampionId)
            {
                card.SetAvatarOnly(matchInfo.Player.Avatar);
                card.CurrentChampionId = matchInfo.Player.ChampionId;
            }
        }

        /// <summary>
        /// 创建新卡片
        /// </summary>
        private void CreateNewCard(PlayerMatchInfo matchInfo, int row, int col, bool isMyTeam)
        {
            // 移除旧控件
            RemoveOldControl(col, row);

            // 创建卡片
            var panel = CreateCardPanel(row);
            var card = CreateCardControl(matchInfo, row);

            panel.Controls.Add(card);
            _form.tableLayoutPanel1.Controls.Add(panel, col, row);

            // 缓存映射
            _cardBySummonerId[matchInfo.Player.SummonerId] = card;
        }

        /// <summary>
        /// 移除旧控件
        /// </summary>
        private void RemoveOldControl(int col, int row)
        {
            var oldControl = _form.tableLayoutPanel1.GetControlFromPosition(col, row);
            if (oldControl != null)
            {
                _form.tableLayoutPanel1.Controls.Remove(oldControl);
                oldControl.Dispose();
            }
        }

        /// <summary>
        /// 创建卡片面板
        /// </summary>
        private BorderPanel CreateCardPanel(int row)
        {
            return new BorderPanel
            {
                BorderColor = GetTeamColor(row),
                BorderWidth = 1,
                Padding = new Padding(2),
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };
        }

        /// <summary>
        /// 创建卡片控件 - 修复：正确设置 HeroIcons
        /// </summary>
        private PlayerCardControl CreateCardControl(PlayerMatchInfo matchInfo, int row)
        {
            var card = new PlayerCardControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                CurrentSummonerId = matchInfo.Player.SummonerId,
                CurrentChampionId = matchInfo.Player.ChampionId
            };

            var player = matchInfo.Player;

            // 【关键修复】设置 HeroIcons 到 ListViewControl
            card.ListViewControl.SmallImageList = matchInfo.HeroIcons;
            card.ListViewControl.View = View.Details;

            card.SetPlayerInfo(
                player.GameName,
                player.SoloRank,
                player.FlexRank,
                player.Avatar,
                player.IsPublic,
                matchInfo.MatchItems,
                player.NameColor,
                player.SummonerId,
                player.ChampionId
            );

            return card;
        }

        #endregion

        #region 3. 数据填充

        /// <summary>
        /// 填充玩家战绩信息 - 修复：恢复组队检测逻辑
        /// </summary>
        public async Task FillPlayerMatchInfoAsync(JArray team, bool isMyTeam, int row)
        {
            try
            {
                if (team == null || team.Count == 0) return;

                //Debug.WriteLine($"[填充数据] 开始 - 行:{row}, 人数:{team.Count}");

                // 使用信号量控制并发
                var semaphore = new SemaphoreSlim(3);
                var tasks = new List<Task<PlayerMatchInfo?>>();

                foreach (var player in team)
                {
                    if (player == null) continue;

                    await semaphore.WaitAsync();

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            return await ProcessSinglePlayer(player, row, isMyTeam);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                var results = await Task.WhenAll(tasks);
                var fetchedInfos = results.Where(info => info != null).ToList();

                // 【关键修复】恢复组队关系检测和颜色更新
                if (fetchedInfos.Count > 0)
                {
                    var detector = new PartyDetector();
                    detector.Detect(fetchedInfos);  // 这会设置每个PlayerInfo的NameColor

                    // ⚠️ 重要：更新UI中的名字颜色
                    foreach (var info in fetchedInfos)
                    {
                        if (info?.Player == null) continue;
                        UpdatePlayerNameColor(info.Player.SummonerId, info.Player.NameColor);
                    }
                }

                // 防空白兜底
                await CheckAndFillEmptyCards(team, row, isMyTeam);

                //Debug.WriteLine($"[填充数据] 完成 - 行:{row}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[填充数据] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理单个玩家
        /// </summary>
        private async Task<PlayerMatchInfo?> ProcessSinglePlayer(JToken player, int row, bool isMyTeam)
        {
            try
            {
                long sid = player["summonerId"]?.Value<long>() ?? 0;
                int cid = player["championId"]?.Value<int>() ?? 0;

                // 检查是否为隐藏玩家
                string visibility = player["nameVisibilityType"]?.ToString() ?? "UNHIDDEN";
                bool isHidden = visibility == "HIDDEN" || sid == 0;

                // ⚠️ 修复：不再跳过隐藏玩家
                // 对于隐藏玩家，直接返回一个占位信息
                if (isHidden)
                {
                    return new PlayerMatchInfo
                    {
                        Player = new PlayerInfo
                        {
                            SummonerId = sid,
                            ChampionId = cid,
                            ChampionName = Globals.resLoading.GetChampionById(cid)?.Name ?? "未知",
                            Avatar = LoadChampionAvatar(cid),  // ✅ 隐藏玩家的英雄头像正常显示
                            GameName = "隐藏玩家",
                            SoloRank = "隐藏",
                            FlexRank = "隐藏",
                            IsPublic = "隐藏",
                            NameColor = GetTeamColor(row)
                        },
                        MatchItems = new List<ListViewItem>(),
                        HeroIcons = new ImageList()
                    };
                }

                // 非隐藏玩家：检查缓存
                PlayerMatchInfo? info = CheckCache(sid, cid);
                if (info != null) return info;

                // 查询玩家信息
                info = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(player);
                if (info == null || info.Player == null)
                {
                    // 查询失败时返回失败信息
                    return CreateFailedPlayerInfoWithRow(sid, cid, row);
                }

                // 更新缓存
                UpdatePlayerCache(sid, info);

                // 更新UI
                await UpdatePlayerCardUI(sid, info, row, isMyTeam);

                return info;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[处理单个玩家] 异常: {ex.Message}");
                return CreateFailedPlayerInfoWithRow(0, 0, row);
            }
        }

        /// <summary>
        /// 检查缓存
        /// </summary>
        private PlayerMatchInfo? CheckCache(long sid, int cid)
        {
            lock (_cachedPlayerMatchInfos)
            {
                if (_cachedPlayerMatchInfos.TryGetValue(sid, out var cachedInfo) &&
                    _currentChampBySummoner.TryGetValue(sid, out int current) && current == cid)
                {
                    return cachedInfo; // 缓存有效
                }
            }
            return null;
        }

        /// <summary>
        /// 更新玩家缓存
        /// </summary>
        private void UpdatePlayerCache(long sid, PlayerMatchInfo info)
        {
            lock (_cachedPlayerMatchInfos)
            {
                _cachedPlayerMatchInfos[sid] = info;
            }
        }

        /// <summary>
        /// 更新玩家卡片UI
        /// </summary>
        private async Task UpdatePlayerCardUI(long sid, PlayerMatchInfo info, int row, bool isMyTeam)
        {
            if (!_summonerToColMap.TryGetValue(sid, out int col)) return;

            // 确认英雄没换
            if (!_currentChampBySummoner.TryGetValue(sid, out int curCid) ||
                curCid != info.Player.ChampionId)
            {
                Debug.WriteLine($"[跳过] sid={sid} 英雄已变更");
                return;
            }

            // 更新卡片
            await Task.Run(() => _form.Invoke(() =>
            {
                CreateLoadingPlayerMatch(info, isMyTeam, row, col);
            }));
        }

        /// <summary>
        /// 检查并填充缺失的卡片（重命名以避免冲突）
        /// </summary>
        private async Task CheckAndFillEmptyCards(JArray team, int row, bool isMyTeam)
        {
            for (int col = 0; col < team.Count; col++)
            {
                if (!playerCache.TryGetValue((row, col), out var cachedPlayer)) continue;

                long sid = cachedPlayer.summonerId;
                int cid = cachedPlayer.championId;

                var ctrl = _form.tableLayoutPanel1.GetControlFromPosition(col, row);

                if (ctrl == null || ctrl.Controls.Count == 0)
                {
                    Debug.WriteLine($"[防空白] 位置 ({row},{col}) 为空，补失败卡");
                    var failCard = CreateFailedPlayerInfo(sid, cid);
                    await Task.Run(() => _form.Invoke(() =>
                    {
                        CreateLoadingPlayerMatch(failCard, isMyTeam, row, col);
                    }));
                }
            }
        }

        #endregion

        #region 4. 卡片创建（供外部调用）

        /// <summary>
        /// 创建加载中的玩家卡片（供外部调用）- 修复：确保设置 HeroIcons
        /// </summary>
        public void CreateLoadingPlayerMatch(PlayerMatchInfo matchInfo, bool isMyTeam, int row, int column)
        {
            try
            {
                _form.Invoke(() =>
                {
                    var player = matchInfo.Player;

                    // 检查是否已有卡片
                    var existingPanel = _form.tableLayoutPanel1.GetControlFromPosition(column, row) as BorderPanel;
                    if (existingPanel != null && existingPanel.Controls.Count > 0)
                    {
                        var existingCard = existingPanel.Controls[0] as PlayerCardControl;
                        if (existingCard != null && existingCard.CurrentSummonerId == player.SummonerId)
                        {
                            // 更新现有卡片
                            UpdateCardContent(existingCard, matchInfo);
                            return;
                        }
                    }

                    // 创建新卡片
                    CreateNewPlayerCard(matchInfo, row, column, isMyTeam);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CreateLoadingPlayerMatch] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新卡片内容 - 修复：设置 HeroIcons
        /// </summary>
        private void UpdateCardContent(PlayerCardControl card, PlayerMatchInfo matchInfo)
        {
            var player = matchInfo.Player;

            // 【关键修复】先设置 HeroIcons
            card.ListViewControl.SmallImageList = matchInfo.HeroIcons;

            card.SetPlayerInfo(
                player.GameName,
                player.SoloRank,
                player.FlexRank,
                player.Avatar,
                player.IsPublic,
                matchInfo.MatchItems,
                player.NameColor,
                player.SummonerId,
                player.ChampionId
            );
        }

        /// <summary>
        /// 创建新玩家卡片 - 修复：设置 HeroIcons
        /// </summary>
        private void CreateNewPlayerCard(PlayerMatchInfo matchInfo, int row, int column, bool isMyTeam)
        {
            Color borderColor = GetTeamColor(row);

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
                Margin = new Padding(0),
                CurrentSummonerId = matchInfo.Player.SummonerId,
                CurrentChampionId = matchInfo.Player.ChampionId
            };

            var player = matchInfo.Player;
            string name = player.GameName ?? "未知";
            string soloRank = string.IsNullOrEmpty(player.SoloRank) ? "未知" : player.SoloRank;
            string flexRank = string.IsNullOrEmpty(player.FlexRank) ? "未知" : player.FlexRank;

            // 【关键修复】先设置 HeroIcons
            card.ListViewControl.SmallImageList = matchInfo.HeroIcons;
            card.ListViewControl.View = View.Details;

            card.SetPlayerInfo(
                name,
                soloRank,
                flexRank,
                player.Avatar,
                player.IsPublic,
                matchInfo.MatchItems,
                player.NameColor,
                player.SummonerId,
                player.ChampionId
            );

            panel.Controls.Add(card);

            // 添加到表格
            var oldControl = _form.tableLayoutPanel1.GetControlFromPosition(column, row);
            if (oldControl != null)
            {
                _form.tableLayoutPanel1.Controls.Remove(oldControl);
                oldControl.Dispose();
            }

            _form.tableLayoutPanel1.Controls.Add(panel, column, row);
            _cardBySummonerId[matchInfo.Player.SummonerId] = card;
        }

        #endregion

        #region 5. 卡片丢失补全
        /// <summary>
        /// 检查并补全缺失的卡片（增强版：支持精确位置跟踪）
        /// </summary>
        public async Task CheckAndCompleteMissingCards(JArray fullSession, bool isMyTeamPhase = false,
            bool isChampSelectPhase = false, int retryCount = 2)
        {
            if (fullSession == null || fullSession.Count == 0)
            {
                Debug.WriteLine("[补全检查] 完整session数据为空");
                return;
            }

            // 保存完整数据供后续使用
            _fullSessionData = fullSession;

            try
            {
                // 阶段标识
                string phaseName = isMyTeamPhase ? "选人阶段" : "游戏阶段";
                phaseName += isChampSelectPhase ? "(选人)" : "(游戏)";

                Debug.WriteLine($"[{phaseName}补全] 开始检查缺失卡片，有{fullSession.Count}名玩家数据");

                // 记录开始时间
                var startTime = DateTime.Now;

                // 【重要】如果是选人阶段，需要额外延迟，确保基础卡片已创建
                if (isChampSelectPhase)
                {
                    await Task.Delay(800); // 等待基础卡片创建完成
                }
                else
                {
                    await Task.Delay(500); // 游戏阶段正常延迟
                }

                // 【关键改进】使用增强的位置查找方法
                var missingPositions = FindMissingCardsWithPositionTracking(fullSession, isMyTeamPhase, isChampSelectPhase);

                if (missingPositions.Count == 0)
                {
                    Debug.WriteLine($"[{phaseName}补全] 没有发现缺失的卡片");
                    return;
                }

                Debug.WriteLine($"[{phaseName}补全] 发现 {missingPositions.Count} 个缺失位置");

                // 打印每个缺失位置的详细信息
                foreach (var (row, col, expectedSummonerId, foundSummonerId) in missingPositions)
                {
                    string teamName = row == 0 ? "我方" : "敌方";
                    Debug.WriteLine($"[缺失详情] {teamName}位置({row},{col}): 预期sid={expectedSummonerId}, 实际sid={foundSummonerId}");
                }

                // 尝试从完整session中查找并补全
                bool success = await FillMissingCardsWithPrecision(missingPositions, fullSession, isMyTeamPhase);

                // 计算耗时
                var elapsed = DateTime.Now - startTime;
                Debug.WriteLine($"[{phaseName}补全] 补全完成，耗时{elapsed.TotalMilliseconds}ms，成功={success}");

                // 如果失败且有重试次数，重试
                if (!success && retryCount > 0)
                {
                    Debug.WriteLine($"[{phaseName}补全] 第一次补全失败，等待后重试（剩余重试次数：{retryCount}）");
                    await Task.Delay(1500); // 增加等待时间

                    // 重新检查缺失位置
                    missingPositions = FindMissingCardsWithPositionTracking(fullSession, isMyTeamPhase, isChampSelectPhase);
                    if (missingPositions.Count > 0)
                    {
                        success = await FillMissingCardsWithPrecision(missingPositions, fullSession, isMyTeamPhase);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[补全检查] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 查找缺失卡片位置（增强版：支持隐藏玩家判断）
        /// </summary>
        private List<(int row, int col, long expectedSummonerId, long foundSummonerId)>
            FindMissingCardsWithPositionTracking(JArray fullSession, bool isMyTeamPhase, bool isChampSelectPhase = false)
        {
            var missing = new List<(int row, int col, long expectedSummonerId, long foundSummonerId)>();

            try
            {
                // 获取UI卡片信息
                var uiCards = BuildUICardPositions();

                // 建立预期位置映射
                var expectedPositions = BuildExpectedPositionMap(fullSession, isMyTeamPhase);

                // 检查每个预期位置
                foreach (var position in expectedPositions)
                {
                    var (row, col) = position.Key;
                    var player = position.Value;
                    long expectedSid = player["summonerId"]?.Value<long>() ?? 0;
                    int expectedChampionId = player["championId"]?.Value<int>() ?? 0;
                    string visibility = player["nameVisibilityType"]?.ToString() ?? "UNHIDDEN";
                    bool isHidden = visibility == "HIDDEN" || expectedSid == 0;

                    // 检查UI中这个位置是否有卡片
                    bool positionHasCard = uiCards.TryGetValue((row, col), out var cardInfo);
                    long actualSid = positionHasCard ? cardInfo.SummonerId : -1;

                    // 判断是否需要补全
                    bool needToComplete = false;
                    string reason = "";

                    if (!positionHasCard)
                    {
                        // 位置完全为空
                        needToComplete = true;
                        reason = "位置完全为空";
                    }
                    else if (cardInfo.IsPlaceholder)
                    {
                        // 位置是占位卡片
                        needToComplete = true;
                        reason = "位置是占位卡片";
                    }
                    else if (isHidden)
                    {
                        // 预期是隐藏玩家，但当前位置不是隐藏玩家
                        if (!cardInfo.IsHiddenPlayer)
                        {
                            needToComplete = true;
                            reason = "预期隐藏玩家但当前位置不是";
                        }
                        // 如果当前位置已经是隐藏玩家，检查英雄是否匹配
                        else if (cardInfo.ChampionId > 0 && expectedChampionId > 0 &&
                                 cardInfo.ChampionId != expectedChampionId)
                        {
                            needToComplete = true;
                            reason = $"隐藏玩家英雄不匹配: 预期{expectedChampionId}, 实际{cardInfo.ChampionId}";
                        }
                    }
                    else if (expectedSid > 0)
                    {
                        // 非隐藏玩家：检查ID是否匹配
                        if (actualSid > 0 && expectedSid != actualSid)
                        {
                            // 检查这个玩家是否在其他位置
                            bool playerExistsElsewhere = uiCards.Any(kvp =>
                                kvp.Value.SummonerId == expectedSid && kvp.Key != (row, col));

                            if (playerExistsElsewhere)
                            {
                                // 玩家在其他位置，说明只是位置调换
                                Debug.WriteLine($"[位置调换] 玩家{expectedSid}在位置({row},{col})被调换");
                            }
                            else
                            {
                                needToComplete = true;
                                reason = $"玩家ID不匹配: 预期{expectedSid}, 实际{actualSid}";
                            }
                        }
                        else if (actualSid <= 0 && !cardInfo.IsHiddenPlayer)
                        {
                            // 位置不是隐藏玩家，但预期有玩家
                            needToComplete = true;
                            reason = $"预期有玩家{expectedSid}但位置不是隐藏玩家";
                        }
                    }

                    if (needToComplete)
                    {
                        missing.Add((row, col, expectedSid, actualSid));
                        Debug.WriteLine($"[缺失检测] 位置({row},{col}): {reason}");
                    }
                }

                // 【新增】检查是否有隐藏玩家被遗漏
                CheckHiddenPlayersMissing(fullSession, uiCards, missing);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[缺失检测] 异常: {ex.Message}");
            }

            return missing;
        }

        /// <summary>
        /// 检查隐藏玩家是否被遗漏
        /// </summary>
        private void CheckHiddenPlayersMissing(JArray fullSession, Dictionary<(int row, int col), UICardInfo> uiCards,
            List<(int row, int col, long expectedSummonerId, long foundSummonerId)> missing)
        {
            try
            {
                // 统计预期中的隐藏玩家
                var expectedHiddenPlayers = new List<(int row, int col, int championId)>();

                foreach (var player in fullSession)
                {
                    long sid = player["summonerId"]?.Value<long>() ?? 0;
                    int championId = player["championId"]?.Value<int>() ?? 0;
                    string visibility = player["nameVisibilityType"]?.ToString() ?? "UNHIDDEN";
                    bool isHidden = visibility == "HIDDEN" || sid == 0;

                    if (isHidden && championId > 0)
                    {
                        int team = player["team"]?.Value<int>() ?? -1;
                        int cellId = player["cellId"]?.Value<int>() ?? -1;

                        if (team != -1 && cellId != -1)
                        {
                            int row = team == 1 ? 0 : 1;
                            int col = cellId % 5;

                            expectedHiddenPlayers.Add((row, col, championId));
                            Debug.WriteLine($"[隐藏玩家] 预期位置({row},{col})有隐藏玩家，英雄{championId}");
                        }
                    }
                }

                // 检查每个预期隐藏玩家的位置
                foreach (var (row, col, championId) in expectedHiddenPlayers)
                {
                    if (uiCards.TryGetValue((row, col), out var cardInfo))
                    {
                        if (cardInfo.IsHiddenPlayer)
                        {
                            // 已经是隐藏玩家，检查英雄是否匹配
                            if (cardInfo.ChampionId > 0 && championId > 0 &&
                                cardInfo.ChampionId != championId)
                            {
                                missing.Add((row, col, 0, cardInfo.SummonerId));
                                Debug.WriteLine($"[隐藏英雄不匹配] 位置({row},{col}): 预期英雄{championId}, 实际英雄{cardInfo.ChampionId}");
                            }
                        }
                        else
                        {
                            // 位置不是隐藏玩家
                            missing.Add((row, col, 0, cardInfo.SummonerId));
                            Debug.WriteLine($"[隐藏玩家缺失] 位置({row},{col})应该有隐藏玩家但实际不是");
                        }
                    }
                    else
                    {
                        // 位置完全为空
                        missing.Add((row, col, 0, -1));
                        Debug.WriteLine($"[隐藏玩家缺失] 位置({row},{col})应该有隐藏玩家但位置为空");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[检查隐藏玩家] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 建立当前UI中所有卡片的详细信息
        /// </summary>
        private Dictionary<(int row, int col), UICardInfo> BuildUICardPositions()
        {
            var uiCards = new Dictionary<(int row, int col), UICardInfo>();

            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    var control = _form.tableLayoutPanel1.GetControlFromPosition(col, row);
                    var cardInfo = new UICardInfo();

                    if (control is BorderPanel borderPanel && borderPanel.Controls.Count > 0)
                    {
                        var card = borderPanel.Controls[0] as PlayerCardControl;
                        if (card != null && !card.IsDisposed)
                        {
                            cardInfo.SummonerId = card.CurrentSummonerId;
                            cardInfo.ChampionId = card.CurrentChampionId;
                            cardInfo.DisplayName = card.lblPlayerName.Text;
                            cardInfo.IsHiddenPlayer = cardInfo.DisplayName == "隐藏玩家" ||
                                                     cardInfo.DisplayName == "玩家未找到" ||
                                                     cardInfo.DisplayName == "查询失败";
                            cardInfo.IsPlaceholder = cardInfo.DisplayName == "玩家未找到" ||
                                                    cardInfo.DisplayName == "查询失败" ||
                                                    cardInfo.SummonerId < 0;

                            // 获取英雄名称
                            if (cardInfo.ChampionId > 0)
                            {
                                cardInfo.ChampionName = Globals.resLoading.GetChampionById(cardInfo.ChampionId)?.Name ?? "未知";
                            }

                            uiCards[(row, col)] = cardInfo;

                            Debug.WriteLine($"[UI信息] 位置({row},{col}): sid={cardInfo.SummonerId}, " +
                                           $"champ={cardInfo.ChampionId}({cardInfo.ChampionName}), " +
                                           $"name='{cardInfo.DisplayName}', " +
                                           $"hidden={cardInfo.IsHiddenPlayer}, placeholder={cardInfo.IsPlaceholder}");
                        }
                    }
                }
            }

            Debug.WriteLine($"[UI信息] 共找到{uiCards.Count}个卡片信息");
            return uiCards;
        }

        /// <summary>
        /// UI卡片信息类
        /// </summary>
        private class UICardInfo
        {
            public long SummonerId { get; set; }
            public int ChampionId { get; set; }
            public string ChampionName { get; set; }
            public string DisplayName { get; set; }
            public bool IsHiddenPlayer { get; set; }
            public bool IsPlaceholder { get; set; }
        }

        /// <summary>
        /// 建立预期位置映射（最终版：支持隐藏玩家和智能匹配）
        /// </summary>
        private Dictionary<(int row, int col), JToken> BuildExpectedPositionMap(JArray fullSession, bool isMyTeamPhase)
        {
            var positionMap = new Dictionary<(int row, int col), JToken>();

            try
            {
                // 第一步：按队伍和cellId进行基础映射
                foreach (var player in fullSession)
                {
                    int team = player["team"]?.Value<int>() ?? -1;
                    int cellId = player["cellId"]?.Value<int>() ?? -1;
                    long sid = player["summonerId"]?.Value<long>() ?? 0;

                    if (team == -1) continue;

                    int row = team == 1 ? 0 : 1;
                    int col = cellId % 5;

                    // 对于选人阶段且是MyTeam阶段，只处理我方
                    if (isMyTeamPhase && row != 0) continue;

                    positionMap[(row, col)] = player;
                    Debug.WriteLine($"[基础映射] 位置({row},{col}) <- sid={sid}, cellId={cellId}");
                }

                // 第二步：如果有位置冲突，进行智能调整
                AdjustPositionConflicts(positionMap, fullSession, isMyTeamPhase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[建立映射] 异常: {ex.Message}");
            }

            return positionMap;
        }

        /// <summary>
        /// 调整位置冲突
        /// </summary>
        private void AdjustPositionConflicts(Dictionary<(int row, int col), JToken> positionMap,
            JArray fullSession, bool isMyTeamPhase)
        {
            try
            {
                // 检查是否有重复的玩家被映射到多个位置
                var playerPositions = new Dictionary<long, List<(int row, int col)>>();

                foreach (var kvp in positionMap)
                {
                    var player = kvp.Value;
                    long sid = player["summonerId"]?.Value<long>() ?? 0;

                    if (sid > 0)
                    {
                        if (!playerPositions.ContainsKey(sid))
                            playerPositions[sid] = new List<(int row, int col)>();

                        playerPositions[sid].Add(kvp.Key);
                    }
                }

                // 处理有冲突的玩家
                foreach (var kvp in playerPositions)
                {
                    if (kvp.Value.Count > 1)
                    {
                        Debug.WriteLine($"[位置冲突] 玩家{kvp.Key}被映射到{string.Join(",", kvp.Value)}");

                        // 保留第一个位置，其他位置重新分配
                        var firstPosition = kvp.Value[0];
                        for (int i = 1; i < kvp.Value.Count; i++)
                        {
                            var conflictPosition = kvp.Value[i];
                            positionMap.Remove(conflictPosition);
                            Debug.WriteLine($"[冲突解决] 移除冲突位置{conflictPosition}");
                        }
                    }
                }

                // 重新填充被移除的位置
                var emptyPositions = GetEmptyPositions(positionMap, isMyTeamPhase);
                var unassignedPlayers = GetUnassignedPlayers(fullSession, positionMap);

                // 智能分配未分配的玩家到空位置
                foreach (var position in emptyPositions)
                {
                    if (unassignedPlayers.Count == 0) break;

                    var player = unassignedPlayers[0];
                    positionMap[position] = player;
                    unassignedPlayers.RemoveAt(0);

                    long sid = player["summonerId"]?.Value<long>() ?? 0;
                    Debug.WriteLine($"[智能分配] 位置{position} <- 玩家{sid}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[调整冲突] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取空位置
        /// </summary>
        private List<(int row, int col)> GetEmptyPositions(Dictionary<(int row, int col), JToken> positionMap, bool isMyTeamPhase)
        {
            var emptyPositions = new List<(int row, int col)>();

            int maxRow = isMyTeamPhase ? 0 : 1; // 如果是选人阶段只检查我方

            for (int row = 0; row <= maxRow; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    if (!positionMap.ContainsKey((row, col)))
                    {
                        emptyPositions.Add((row, col));
                    }
                }
            }

            return emptyPositions;
        }

        /// <summary>
        /// 获取未分配的玩家
        /// </summary>
        private List<JToken> GetUnassignedPlayers(JArray fullSession, Dictionary<(int row, int col), JToken> positionMap)
        {
            var assignedPlayerIds = new HashSet<long>();

            foreach (var player in positionMap.Values)
            {
                long sid = player["summonerId"]?.Value<long>() ?? 0;
                if (sid > 0) assignedPlayerIds.Add(sid);
            }

            return fullSession
                .Where(p =>
                {
                    long sid = p["summonerId"]?.Value<long>() ?? 0;
                    return sid > 0 && !assignedPlayerIds.Contains(sid);
                })
                .ToList();
        }

        /// <summary>
        /// 精准补全缺失卡片（改进版：只补全真正缺失的）
        /// </summary>
        private async Task<bool> FillMissingCardsWithPrecision(
            List<(int row, int col, long expectedSummonerId, long foundSummonerId)> missingPositions,
            JArray fullSession,
            bool isMyTeamPhase)
        {
            if (missingPositions.Count == 0) return true;

            bool allSuccess = true;
            int successCount = 0;
            int skippedCount = 0;

            Debug.WriteLine($"[精准补全] 开始处理{missingPositions.Count}个可能缺失的位置");

            foreach (var (row, col, expectedSid, foundSid) in missingPositions)
            {
                try
                {
                    string teamName = row == 0 ? "我方" : "敌方";

                    // 检查这个玩家是否已经在UI中的其他位置
                    bool playerAlreadyInUI = IsPlayerAlreadyInUI(expectedSid, (row, col));

                    if (playerAlreadyInUI && foundSid > 0)
                    {
                        // 玩家已经在UI中，只是位置不对，跳过补全
                        Debug.WriteLine($"[跳过调换] {teamName}位置({row},{col})的玩家{expectedSid}已在其他位置");
                        skippedCount++;
                        continue;
                    }

                    if (foundSid == -1) // 位置完全为空
                    {
                        Debug.WriteLine($"[补全处理] {teamName}位置({row},{col})完全为空，需要补全玩家{expectedSid}");
                    }
                    else
                    {
                        Debug.WriteLine($"[补全处理] {teamName}位置({row},{col})玩家{expectedSid}完全缺失");
                    }

                    // 精确查找玩家
                    JToken player = FindPlayerByPositionAndId(row, col, expectedSid, fullSession);

                    if (player == null)
                    {
                        Debug.WriteLine($"[补全失败] 位置({row},{col})找不到对应玩家");
                        allSuccess = false;

                        // 只有在位置完全为空时才创建占位卡片
                        if (foundSid == -1)
                        {
                            await CreatePlaceholderCard(row, col, row == 0);
                        }
                        continue;
                    }

                    long actualSid = player["summonerId"]?.Value<long>() ?? 0;
                    int championId = player["championId"]?.Value<int>() ?? 0;

                    Debug.WriteLine($"[补全找到] 位置({row},{col}): sid={actualSid}, champ={championId}");

                    // 只在位置完全为空时才创建基础卡片
                    if (foundSid == -1)
                    {
                        await Task.Run(() => _form.Invoke(() =>
                        {
                            CreateSingleBasicCard(player, row, col, row == 0);
                        }));
                        successCount++;
                    }

                    // 如果是非隐藏玩家，异步查询详细信息
                    string visibility = player["nameVisibilityType"]?.ToString() ?? "UNHIDDEN";
                    if (visibility != "HIDDEN" && actualSid > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(1000);
                                var info = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(player);
                                if (info != null && info.Player != null)
                                {
                                    await Task.Run(() => _form.Invoke(() =>
                                    {
                                        CreateLoadingPlayerMatch(info, row == 0, row, col);
                                    }));
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[补全查询] 位置({row},{col})查询异常: {ex.Message}");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[补全单个] 位置({row},{col})异常: {ex.Message}");
                    allSuccess = false;
                }
            }

            Debug.WriteLine($"[精准补全] 完成: 成功{successCount}个，跳过{skippedCount}个，共{missingPositions.Count}个位置");
            return allSuccess;
        }

        /// <summary>
        /// 检查玩家是否已经在UI中的其他位置（简化版）
        /// </summary>
        private bool IsPlayerAlreadyInUI(long summonerId, (int row, int col) excludePosition)
        {
            var uiCards = BuildUICardPositions();
            return IsPlayerAlreadyInUI(summonerId, 0, false, excludePosition, uiCards);
        }

        /// <summary>
        /// 检查玩家是否已经在UI中的其他位置（完整版）
        /// </summary>
        private bool IsPlayerAlreadyInUI(long summonerId, int championId, bool isHidden,
            (int row, int col) excludePosition, Dictionary<(int row, int col), UICardInfo> uiCards)
        {
            foreach (var kvp in uiCards)
            {
                var position = kvp.Key;
                var cardInfo = kvp.Value;

                if (position.row == excludePosition.row && position.col == excludePosition.col)
                    continue;

                if (isHidden)
                {
                    // 对于隐藏玩家，通过英雄ID判断
                    if (cardInfo.IsHiddenPlayer && championId > 0 && cardInfo.ChampionId == championId)
                    {
                        return true;
                    }
                }
                else if (summonerId > 0)
                {
                    // 对于非隐藏玩家，通过summonerId判断
                    if (cardInfo.SummonerId == summonerId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 根据位置和ID精确查找玩家
        /// </summary>
        private JToken FindPlayerByPositionAndId(int row, int col, long expectedSid, JArray fullSession)
        {
            if (fullSession == null) return null;

            // 策略1：如果有expectedSid，优先按ID查找
            if (expectedSid > 0)
            {
                var playerById = fullSession.FirstOrDefault(p =>
                    p["summonerId"]?.Value<long>() == expectedSid);

                if (playerById != null)
                {
                    Debug.WriteLine($"[精确查找] 通过ID找到玩家: sid={expectedSid}");
                    return playerById;
                }
            }

            // 策略2：按位置查找
            int expectedTeam = row == 0 ? 1 : 2; // 行0对应team 1，行1对应team 2

            var teamPlayers = fullSession
                .Where(p => p["team"]?.Value<int>() == expectedTeam)
                .ToList();

            if (col < teamPlayers.Count)
            {
                var playerByPosition = teamPlayers[col];
                Debug.WriteLine($"[精确查找] 通过位置找到玩家: 队伍{expectedTeam}第{col}个");
                return playerByPosition;
            }

            // 策略3：返回第一个匹配队伍的玩家
            return teamPlayers.FirstOrDefault();
        }

        /// <summary>
        /// 创建占位卡片（当找不到玩家时）
        /// </summary>
        private async Task CreatePlaceholderCard(int row, int col, bool isMyTeam)
        {
            try
            {
                // 创建一个特殊ID，避免冲突
                long placeholderId = -1000 - (row * 100 + col * 10);

                Debug.WriteLine($"[创建占位] 位置({row},{col})创建占位卡片，ID={placeholderId}");

                // 更新缓存映射
                UpdateCacheMappings(placeholderId, 0, row, col);

                // 创建占位信息
                var placeholderInfo = new PlayerMatchInfo
                {
                    Player = new PlayerInfo
                    {
                        SummonerId = placeholderId,
                        ChampionId = 0,
                        ChampionName = "位置空",
                        Avatar = LoadDefaultAvatar(),
                        GameName = "玩家未找到",
                        SoloRank = "---",
                        FlexRank = "---",
                        IsPublic = "[未知]",
                        NameColor = GetTeamColor(row)
                    },
                    MatchItems = new List<ListViewItem>(),
                    HeroIcons = new ImageList()
                };

                // 创建卡片
                await Task.Run(() => _form.Invoke(() =>
                {
                    CreateLoadingPlayerMatch(placeholderInfo, isMyTeam, row, col);
                }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[创建占位卡片] 异常: {ex.Message}");
            }
        }
        #endregion

        #region 6. 辅助方法

        /// <summary>
        /// 加载英雄头像
        /// </summary>
        private Image LoadChampionAvatar(int championId)
        {
            try
            {
                return Globals.resLoading.GetChampionIconAsync(championId).GetAwaiter().GetResult()
                    ?? LoadDefaultAvatar();
            }
            catch
            {
                return LoadDefaultAvatar();
            }
        }

        /// <summary>
        /// 加载默认头像
        /// </summary>
        private Image LoadDefaultAvatar()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Defaults", "Profile.png");
            return File.Exists(path) ? Image.FromFile(path) : new Bitmap(64, 64);
        }

        /// <summary>
        /// 获取队伍颜色
        /// </summary>
        private Color GetTeamColor(int row)
        {
            // 注意：这里应该和 PartyDetector 中的颜色逻辑一致
            // PartyDetector 检测组队后会设置不同的颜色
            // 如果没有组队，使用默认的队伍颜色
            return row == 0 ? Color.Red : row == 1 ? Color.Blue : Color.Gray;
        }

        /// <summary>
        /// 更新玩家名字颜色
        /// </summary>
        private void UpdatePlayerNameColor(long summonerId, Color color)
        {
            if (_cardBySummonerId.TryGetValue(summonerId, out var card))
            {
                FormUiStateManager.SafeInvoke(card, () =>
                {
                    if (card.IsDisposed) return;

                    // 更新LinkLabel的颜色
                    card.lblPlayerName.LinkColor = color;
                    card.lblPlayerName.VisitedLinkColor = color;
                    card.lblPlayerName.ActiveLinkColor = color;
                });
            }
            else
            {
                Debug.WriteLine($"[更新颜色] 未找到卡片: summonerId={summonerId}");
            }
        }

        /// <summary>
        /// 创建失败时的玩家信息（原始版本）
        /// </summary>
        public PlayerMatchInfo CreateFailedPlayerInfo(long summonerId, int championId)
        {
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
                    Avatar = LoadDefaultAvatar(),
                    NameColor = Color.Black // 失败时使用黑色
                },
                MatchItems = new List<ListViewItem>(),
                HeroIcons = new ImageList()
            };
        }

        /// <summary>
        /// 创建失败时的玩家信息（带row参数版本）
        /// </summary>
        public PlayerMatchInfo CreateFailedPlayerInfoWithRow(long summonerId, int championId, int row)
        {
            return new PlayerMatchInfo
            {
                Player = new PlayerInfo
                {
                    SummonerId = summonerId,
                    ChampionId = championId,
                    ChampionName = championId > 0 ?
                        Globals.resLoading.GetChampionById(championId)?.Name ?? "未知" : "查询失败",
                    Avatar = LoadChampionAvatar(championId),
                    GameName = "查询失败",
                    IsPublic = "[失败]",
                    SoloRank = "失败",
                    FlexRank = "失败",
                    NameColor = GetTeamColor(row)
                },
                MatchItems = new List<ListViewItem>(),
                HeroIcons = new ImageList()
            };
        }

        /// <summary>
        /// 获取缓存中的玩家信息
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
            _cachedPlayerMatchInfos.Clear();
            _currentChampBySummoner.Clear();
            _summonerToColMap.Clear();
            playerCache.Clear();
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
            playerCache.Clear();
            _cardBySummonerId.Clear();

            // 新增清理
            _positionInfo.Clear();
            _queriedPlayers.Clear();
            _fullSessionData = null;

            // 清理位置信息缓存
            _positionInfo.Clear();
            _queriedPlayers.Clear();
            Debug.WriteLine("[清理状态] 已清除所有缓存和状态");
        }
        #endregion

        #region 7. 内部类

        /// <summary>
        /// 存储查询过的玩家信息，用于后续补全卡片丢失（改为内部类）
        /// </summary>
        private class PlayerBasicInfo
        {
            public long SummonerId { get; set; }
            public long ObfuscatedId { get; set; }
            public int ChampionId { get; set; }
            public string GameName { get; set; }
            public string Visibility { get; set; }
            public bool IsPlaceholder { get; set; }
            public DateTime LastUpdated { get; set; }
        }

        #endregion
    }
}