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

            // 新增：用于避免同一局重复保存的局ID（从team中取gameId，如果没有用时间戳）
            long gameId = 0;
            if (team.Count > 0)
            {
                gameId = team[0]?["gameId"]?.Value<long>() ?? 0;
            }
            if (gameId == 0) gameId = DateTime.Now.Ticks; // 兜底

            // 检查是否有 summonerId == 0 的玩家
            var zeroIdPlayers = team.Where(p => (p["summonerId"]?.Value<long>() ?? 0) == 0).ToList();
            if (zeroIdPlayers.Any())
            {
                string teamType = isMyTeam ? "MyTeam" : "EnemyTeam";
                string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "playerErr");
                Directory.CreateDirectory(folderPath);

                string fileName = $"{teamType}_summonerId0_GameId{gameId}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                string fullPath = Path.Combine(folderPath, fileName);

                try
                {
                    // 保存传入的 team 数据（就是原始 session 中的 myTeam 或 enemyTeam）
                    File.WriteAllText(fullPath, team.ToString(Newtonsoft.Json.Formatting.Indented));
                    Debug.WriteLine($"[调试保存] {teamType} 发现 {zeroIdPlayers.Count} 个 summonerId=0，已保存 team 数据到: {fullPath}");

                    // 额外打印关键信息
                    foreach (var p in zeroIdPlayers)
                    {
                        string puuid = p["puuid"]?.ToString() ?? "";
                        int champId = p["championId"]?.Value<int>() ?? 0;
                        Debug.WriteLine($"[隐藏玩家] summonerId=0, puuid={(string.IsNullOrEmpty(puuid) ? "空" : "有")}, championId={champId}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[调试保存失败] {teamType} 保存异常: {ex.Message}");
                }
            }

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

        #region 缓存管理
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
                    Avatar = LoadErrorImage()
                },
                MatchItems = new List<ListViewItem>(),
                HeroIcons = new ImageList()
            };
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