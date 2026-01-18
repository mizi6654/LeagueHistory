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
        /// 检查并补全缺失的卡片（使用完整session数据）
        /// </summary>
        public async Task CheckAndCompleteMissingCards(JArray fullSession)
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
                // 分析当前UI状态
                var missingPositions = FindMissingCardPositions();

                if (missingPositions.Count == 0)
                {
                    Debug.WriteLine("[补全检查] 没有发现缺失的卡片");
                    return;
                }

                Debug.WriteLine($"[补全检查] 发现 {missingPositions.Count} 个缺失位置");

                // 尝试从完整session中查找并补全
                await FillMissingCardsFromFullSession(missingPositions);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[补全检查] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 查找缺失卡片的位置
        /// </summary>
        private List<(int row, int col)> FindMissingCardPositions()
        {
            var missing = new List<(int row, int col)>();

            // 检查TableLayoutPanel中的空位
            for (int row = 0; row < 2; row++) // 假设2行：0=我方，1=敌方
            {
                for (int col = 0; col < 5; col++) // 每队5人
                {
                    var control = _form.tableLayoutPanel1.GetControlFromPosition(col, row);

                    // 位置为空或者控件有问题
                    if (control == null || control.Controls.Count == 0)
                    {
                        missing.Add((row, col));
                        Debug.WriteLine($"[查找缺失] 位置({row},{col})为空");
                    }
                }
            }

            return missing;
        }

        /// <summary>
        /// 从完整session中补全缺失卡片
        /// </summary>
        private async Task FillMissingCardsFromFullSession(List<(int row, int col)> missingPositions)
        {
            if (_fullSessionData == null) return;

            foreach (var (row, col) in missingPositions)
            {
                try
                {
                    // 从完整session中查找对应位置的玩家
                    var player = FindPlayerInFullSession(row, col);

                    if (player == null)
                    {
                        Debug.WriteLine($"[补全] 位置({row},{col})在完整session中找不到对应玩家");
                        continue;
                    }

                    long sid = player["summonerId"]?.Value<long>() ?? 0;
                    int cid = player["championId"]?.Value<int>() ?? 0;

                    // 检查是否已经查询过
                    if (sid > 0 && _queriedPlayers.Contains(sid))
                    {
                        Debug.WriteLine($"[补全] 位置({row},{col})的玩家{sid}已查询过，跳过");
                        continue;
                    }

                    Debug.WriteLine($"[补全] 为位置({row},{col})创建卡片: sid={sid}, champ={cid}");

                    // 创建卡片
                    await Task.Run(() => _form.Invoke(() =>
                    {
                        CreateSingleBasicCard(player, row, col, row == 0); // row==0是我方
                    }));

                    // 如果是非隐藏玩家，异步查询详细信息
                    string visibility = player["nameVisibilityType"]?.ToString() ?? "UNHIDDEN";
                    if (visibility != "HIDDEN" && sid > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
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
                }
            }
        }

        /// <summary>
        /// 在完整session中查找对应位置的玩家
        /// </summary>
        private JToken FindPlayerInFullSession(int targetRow, int targetCol)
        {
            if (_fullSessionData == null) return null;

            // 遍历完整session中的玩家
            foreach (var player in _fullSessionData)
            {
                int team = player["team"]?.Value<int>() ?? -1;
                int cellId = player["cellId"]?.Value<int>() ?? -1;

                // 确定行号：team 1=蓝色方(我方通常是0行)，team 2=红色方(敌方通常是1行)
                // 需要根据实际情况调整映射
                int row = (team == 1) ? 0 : 1;
                int col = cellId % 5; // cellId 0-4是蓝色方，5-9是红色方

                if (row == targetRow && col == targetCol)
                {
                    return player;
                }
            }

            return null;
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