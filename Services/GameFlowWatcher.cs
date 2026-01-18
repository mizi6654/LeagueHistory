using League.Clients;
using League.Extensions;
using League.Managers;
using League.Networking;
using League.Parsers;
using League.PrimaryElection;
using League.States;
using League.UIState;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    /// <summary>
    /// 负责监听游戏流程阶段（Gameflow Phase），并在不同阶段执行对应操作
    /// </summary>
    public class GameFlowWatcher
    {
        private readonly FormMain _form;
        private readonly FormUiStateManager _uiManager;
        private readonly PlayerCardManager _cardManager;
        private readonly MatchQueryProcessor _matchQueryProcessor;

        // 取消令牌
        private CancellationTokenSource? _watcherCts;
        private CancellationTokenSource? _champSelectCts;

        // 状态标志
        private bool _gameEndHandled = false;
        private bool _hasAutoPreliminated = false;
        private bool _hasSwappedInAram = false;

        // 优化游戏模式设置（避免重复输出）
        private string _lastQueueId = "";

        public GameFlowWatcher(FormMain form, FormUiStateManager uiManager,
            PlayerCardManager cardManager, MatchQueryProcessor matchQueryProcessor)
        {
            _form = form;
            _uiManager = uiManager;
            _cardManager = cardManager;
            _matchQueryProcessor = matchQueryProcessor;
        }

        #region 1. 全局游戏流程监听

        /// <summary>
        /// 启动游戏流程监听
        /// </summary>
        public async void StartGameflowWatcher()
        {
            if (!_uiManager.LcuReady) return;

            _watcherCts = new CancellationTokenSource();
            var token = _watcherCts.Token;

            try
            {
                await Task.Run(async () =>
                {
                    string? lastPhase = null;

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            string? phase = await Globals.lcuClient.GetGameflowPhase();

                            if (string.IsNullOrEmpty(phase))
                            {
                                OnLcuDisconnected();
                                return;
                            }

                            if (phase != lastPhase)
                            {
                                await HandleGameflowPhase(phase, lastPhase);
                                lastPhase = phase;
                            }

                            await Task.Delay(1000, token);
                        }
                        catch (TaskCanceledException) { return; }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[GameflowWatcher] 轮询异常：{ex}");
                            await Task.Delay(2000, token);
                        }
                    }
                }, token);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameflowWatcher] 严重异常：{ex}");
            }
        }

        /// <summary>
        /// 停止所有轮询任务
        /// </summary>
        public void StopGameflowWatcher()
        {
            _watcherCts?.Cancel();
            _watcherCts?.Dispose();
            _watcherCts = null;

            _champSelectCts?.Cancel();
            _champSelectCts?.Dispose();
            _champSelectCts = null;
        }

        /// <summary>
        /// 处理游戏阶段变化
        /// </summary>
        public async Task HandleGameflowPhase(string phase, string? previousPhase)
        {
            Debug.WriteLine($"[游戏阶段] {previousPhase} → {phase}");

            switch (phase)
            {
                case "Matchmaking":
                case "ReadyCheck":
                    await HandleMatchmakingPhase();
                    break;

                case "ChampSelect":
                    await HandleChampSelectPhase();
                    break;

                case "InProgress":
                    await HandleInProgressPhase();
                    break;

                case "EndOfGame":
                case "PreEndOfGame":
                case "WaitingForStats":
                case "Lobby":
                case "None":
                    await HandleGameEndPhase(previousPhase);
                    break;
            }
        }

        #endregion

        #region 2. 各阶段具体处理

        /// <summary>
        /// 匹配阶段处理
        /// </summary>
        private async Task HandleMatchmakingPhase()
        {
            _uiManager.IsGame = false;

            // 清空缓存，准备新游戏
            _cardManager.ClearAllCaches();
            _cardManager.ClearGameState();
            _matchQueryProcessor.ClearPlayerMatchCache();

            // 切换到第二个Tab页并更新UI状态
            FormUiStateManager.SafeInvoke(_form.imageTabControl1, () =>
            {
                // 1. 先切换到第二个Tab
                _form.imageTabControl1.SelectedIndex = 1;

                // 2. 更新UI状态（显示"正在等待加入游戏"）
                _uiManager.SetLcuUiState(_uiManager.LcuReady, _uiManager.IsGame);
            });
        }

        /// <summary>
        /// 选人阶段处理（核心）
        /// </summary>
        private async Task HandleChampSelectPhase()
        {
            _uiManager.IsGame = true;

            // 【一步到位，强制隐藏 + 立即重绘，杜绝残影】
            FormUiStateManager.SafeInvoke(_form, () =>
            {
                // 移除等待面板
                if (_form._waitingPanel != null)
                {
                    if (_form.penalGameMatchData.Controls.Contains(_form._waitingPanel))
                    {
                        _form.penalGameMatchData.Controls.Remove(_form._waitingPanel);
                    }
                    _form._waitingPanel.Dispose();
                    _form._waitingPanel = null;
                }

                // 准备对战面板（清空 + 显示）
                _form.tableLayoutPanel1.Controls.Clear();
                _form.tableLayoutPanel1.Visible = true;
                _form.tableLayoutPanel1.Dock = DockStyle.Fill;
                if (!_form.penalGameMatchData.Controls.Contains(_form.tableLayoutPanel1))
                {
                    _form.penalGameMatchData.Controls.Add(_form.tableLayoutPanel1);
                }

                // 【关键：强制立即重绘，清除残影】
                _form.penalGameMatchData.SuspendLayout();
                _form.penalGameMatchData.Refresh();
                _form.penalGameMatchData.ResumeLayout(true);
            });

            // 立即启动选人阶段详细逻辑（不等待）
            _ = StartChampSelectProcessing();
        }

        /// <summary>
        /// 游戏进行阶段处理 - 修复：添加 try-catch 防止异常传播
        /// </summary>
        private async Task HandleInProgressPhase()
        {
            try
            {
                // 安全地取消选人阶段轮询
                if (_champSelectCts != null)
                {
                    try
                    {
                        _champSelectCts.Cancel();
                    }
                    catch { } // 忽略取消异常
                }

                await ShowEnemyTeamCards();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HandleInProgressPhase] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 游戏结束阶段处理
        /// </summary>
        private async Task HandleGameEndPhase(string? previousPhase)
        {
            if (!_gameEndHandled && IsValidPreviousPhase(previousPhase))
            {
                _gameEndHandled = true;
                await OnGameEnd();
            }
        }

        #endregion

        #region 3. 选人阶段核心逻辑 - 修复取消逻辑

        /// <summary>
        /// 启动选人阶段处理 - 修复：改进取消逻辑
        /// </summary>
        private async Task StartChampSelectProcessing()
        {
            // 重置本局状态
            ResetGameState();

            // 取消现有的选人轮询
            if (_champSelectCts != null)
            {
                try
                {
                    _champSelectCts.Cancel();
                    _champSelectCts.Dispose();
                }
                catch { } // 忽略异常
            }

            _champSelectCts = new CancellationTokenSource();
            var token = _champSelectCts.Token;

            try
            {
                // 先短暂等待，确保UI清理完成
                await Task.Delay(300, token);

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var currentPhase = await Globals.lcuClient.GetGameflowPhase();
                        if (currentPhase != "ChampSelect") break;

                        // 更新我方队伍卡片
                        await UpdateMyTeamCards();

                        // 尝试自动预选
                        await TryAutoPreliminaryAsync();

                        // 根据模式设置轮询间隔
                        await Task.Delay(GetPollingDelay(), token);
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.WriteLine("[选人轮询] 任务被取消");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[选人轮询] 异常: {ex.Message}");
                        await Task.Delay(1000, token);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[StartChampSelectProcessing] 任务被取消");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartChampSelectProcessing] 异常: {ex.Message}");
            }
        }

        #endregion

        #region 4. 队伍卡片显示

        /// <summary>
        /// 更新我方队伍卡片 - 修复：添加 session 获取失败处理
        /// </summary>
        private async Task UpdateMyTeamCards()
        {
            try
            {
                var session = await Globals.lcuClient.GetChampSelectSession();
                if (session == null)
                {
                    Debug.WriteLine("[UpdateMyTeamCards] 无法获取选人会话");
                    return;
                }

                // ⚠️ 优化：只在队列ID变化时设置
                var queueId = session["queueId"]?.ToString() ?? "";
                if (queueId != _lastQueueId && !string.IsNullOrEmpty(queueId))
                {
                    Globals.CurrGameMod = queueId;
                    _lastQueueId = queueId;
                    Debug.WriteLine($"[游戏模式] 设置为: {queueId}");
                }

                var myTeam = session["myTeam"] as JArray;
                if (myTeam == null || myTeam.Count == 0)
                {
                    Debug.WriteLine("[UpdateMyTeamCards] 我方队伍数据为空");
                    return;
                }

                // 检查是否有变化
                var currentSnapshot = myTeam.Select(p =>
                    $"{p["summonerId"]?.Value<long>() ?? 0}:{p["championId"]?.Value<int>() ?? 0}").ToList();

                // 如果是第一次显示或队伍有变化
                if (_form.lastChampSelectSnapshot.Count == 0 || !_form.lastChampSelectSnapshot.SequenceEqual(currentSnapshot))
                {
                    await ProcessMyTeam(myTeam, currentSnapshot);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateMyTeamCards] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理我方队伍数据
        /// </summary>
        private async Task ProcessMyTeam(JArray myTeam, List<string> currentSnapshot)
        {
            // 保存到缓存
            _form._cachedMyTeam = myTeam;
            _form.lastChampSelectSnapshot = currentSnapshot;

            // 确定行号
            int row = myTeam[0]?["team"]?.Value<int>() == 1 ? 0 : 1;

            // 先创建基础卡片（立即显示英雄头像）
            await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: row);

            // 然后异步填充详细数据
            _ = Task.Run(async () =>
            {
                try
                {
                    await _cardManager.FillPlayerMatchInfoAsync(myTeam, isMyTeam: true, row: row);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[填充我方数据] 异常: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 显示敌方队伍卡片 - 修复：设置游戏模式
        /// </summary>
        private async Task ShowEnemyTeamCards()
        {
            try
            {
                //Debug.WriteLine("[显示敌方队伍] 开始获取敌方数据");

                // 获取当前玩家信息
                var currentSummoner = await Globals.lcuClient.GetCurrentSummoner();
                if (currentSummoner == null)
                {
                    Debug.WriteLine("[显示敌方队伍] 无法获取当前玩家");
                    return;
                }

                string myPuuid = currentSummoner["puuid"]?.ToString();
                if (string.IsNullOrEmpty(myPuuid))
                {
                    Debug.WriteLine("[显示敌方队伍] 当前玩家puuid为空");
                    return;
                }

                // 获取游戏会话数据
                var sessionData = await Globals.lcuClient.GetGameSession();
                if (sessionData == null)
                {
                    Debug.WriteLine("[显示敌方队伍] 无法获取游戏会话");
                    return;
                }

                // 保存完整session用于补全
                var fullSession = ExtractFullSession(sessionData);

                var teamOne = sessionData["gameData"]?["teamOne"] as JArray;
                var teamTwo = sessionData["gameData"]?["teamTwo"] as JArray;
                if (teamOne == null || teamTwo == null)
                {
                    Debug.WriteLine("[显示敌方队伍] 队伍数据不完整");
                    return;
                }

                // 判断自己在哪一队
                bool isInTeamOne = teamOne.Any(t =>
                {
                    var puuidToken = t["puuid"];
                    return puuidToken != null && puuidToken.ToString() == myPuuid;
                });

                // 选择敌方队伍和行号
                JArray enemyTeam = isInTeamOne ? teamTwo : teamOne;
                int enemyRow = isInTeamOne ? 1 : 0;

                if (enemyTeam == null || enemyTeam.Count == 0)
                {
                    Debug.WriteLine("[显示敌方队伍] 敌方队伍数据为空");
                    return;
                }

                //Debug.WriteLine($"[显示敌方队伍] 找到敌方 {enemyTeam.Count} 人，显示在行 {enemyRow}");

                // 先创建基础卡片
                await _cardManager.CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);

                // 然后异步填充详细数据
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _cardManager.FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[填充敌方数据] 异常: {ex.Message}");
                    }
                });

                // 在显示敌方队伍后，检查并补全缺失卡片
                await Task.Delay(1000); // 等待一下，确保基础卡片已创建

                if (_cardManager != null)
                {
                    await _cardManager.CheckAndCompleteMissingCards(fullSession);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShowEnemyTeamCards] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 提取完整的玩家session数据
        /// </summary>
        private JArray ExtractFullSession(JObject sessionData)
        {
            var fullPlayers = new JArray();

            try
            {
                var teamOne = sessionData["gameData"]?["teamOne"] as JArray;
                var teamTwo = sessionData["gameData"]?["teamTwo"] as JArray;

                if (teamOne != null)
                {
                    foreach (var player in teamOne)
                    {
                        player["team"] = 1; // 标记队伍
                        fullPlayers.Add(player);
                    }
                }

                if (teamTwo != null)
                {
                    foreach (var player in teamTwo)
                    {
                        player["team"] = 2; // 标记队伍
                        fullPlayers.Add(player);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[提取完整session] 异常: {ex.Message}");
            }

            Debug.WriteLine($"[提取完整session] 共找到 {fullPlayers.Count} 名玩家");
            return fullPlayers;
        }
        #endregion

        #region 5. 自动预选功能

        /// <summary>
        /// 尝试自动预选
        /// </summary>
        private async Task TryAutoPreliminaryAsync()
        {
            try
            {
                // 检查是否启用自动预选
                var preConfig = _form.GetAppConfig()?.Preliminary;
                if (preConfig == null || !preConfig.EnableAutoPreliminary)
                    return;

                // 获取预选列表
                var preList = await _form.GetPreSelectedHeroesAsync();
                if (!preList.Any()) return;

                // 获取当前会话
                var session = await Globals.lcuClient.GetChampSelectSession();
                if (session == null) return;

                int queueId = session["queueId"]?.Value<int>() ?? 0;

                // 检查是否在当前模式启用
                if (!IsModeEnabled(queueId)) return;

                // 执行预选逻辑
                await ExecutePreliminaryLogic(queueId, preList);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TryAutoPreliminaryAsync] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查当前模式是否启用
        /// </summary>
        private bool IsModeEnabled(int queueId)
        {
            var config = _form.GetAppConfig();
            if (config == null) return false;

            return queueId switch
            {
                400 or 430 => config.EnablePreliminaryInNormal,
                420 or 440 => config.EnablePreliminaryInRanked,
                450 => config.EnablePreliminaryInAram,
                2400 => config.EnablePreliminaryInNexusBlitz,
                _ => false
            };
        }

        /// <summary>
        /// 执行预选逻辑
        /// </summary>
        private async Task ExecutePreliminaryLogic(int queueId, List<PreliminaryHero> preList)
        {
            // ARAM类模式：持续抢英雄
            if (queueId == 450 || queueId == 2400)
            {
                await Globals.lcuClient.AutoSwapToHighestPriorityAsync(preList);
                return;
            }

            // 普通模式：只声明一次意图
            if (_hasAutoPreliminated) return;

            bool success = await Globals.lcuClient.AutoDeclareIntentAsync(preList);
            if (success)
            {
                _hasAutoPreliminated = true;
                Debug.WriteLine("[自动预选] 普通模式意图声明成功");
            }
        }

        #endregion

        #region 6. 游戏结束与清理

        /// <summary>
        /// 游戏结束处理
        /// </summary>
        private async Task OnGameEnd()
        {
            Debug.WriteLine("游戏已结束，正在清理...");

            // 清理所有缓存
            _cardManager.ClearAllCaches();

            // 安全地取消选人轮询
            if (_champSelectCts != null)
            {
                try
                {
                    _champSelectCts.Cancel();
                    _champSelectCts.Dispose();
                }
                catch { } // 忽略异常
            }

            _form.lastChampSelectSnapshot.Clear();
            RefreshState.ForceMatchRefresh = true;
            _lastQueueId = ""; // 重置队列ID

            // 重置UI
            await Task.Run(() => _form.Invoke(async () =>
            {
                await _form.InitializeDefaultTab();
            }));
        }

        #endregion

        #region 7. 辅助方法

        /// <summary>
        /// 重置游戏状态
        /// </summary>
        private void ResetGameState()
        {
            _gameEndHandled = false;
            _hasAutoPreliminated = false;
            _hasSwappedInAram = false;
        }

        /// <summary>
        /// 获取轮询延迟时间
        /// </summary>
        private int GetPollingDelay()
        {
            try
            {
                // ARAM类模式需要更快的轮询
                var session = Globals.lcuClient.GetChampSelectSession().Result;
                int queueId = session?["queueId"]?.Value<int>() ?? 0;

                return queueId == 450 || queueId == 2400 ? 500 : 1000;
            }
            catch
            {
                return 1000; // 默认值
            }
        }

        /// <summary>
        /// 检查是否为有效的上一阶段
        /// </summary>
        private bool IsValidPreviousPhase(string? previousPhase)
        {
            return previousPhase == "InProgress" ||
                   previousPhase == "WaitingForStats" ||
                   previousPhase == "ChampSelect";
        }

        /// <summary>
        /// LCU断开连接处理
        /// </summary>
        private void OnLcuDisconnected()
        {
            _uiManager.LcuReady = false;
            _uiManager.IsGame = false;
            _watcherCts?.Cancel();
            _uiManager.SetLcuUiState(false, false);
            _form.StartLcuConnectPolling();
        }
        #endregion
    }
}