using League.Extensions;
using League.Managers;
using League.Parsers;
using League.States;
using League.UIState;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    /// <summary>
    /// 负责监听游戏流程阶段（Gameflow Phase），并在不同阶段执行对应操作
    /// 主要功能：选人阶段显示队伍卡片、自动预选/抢英雄、游戏结束清理等
    /// </summary>
    public class GameFlowWatcher
    {
        private readonly FormMain _form;
        private readonly FormUiStateManager _uiManager;
        private readonly PlayerCardManager _cardManager;
        private readonly MatchQueryProcessor _matchQueryProcessor;

        // 取消令牌：用于控制后台轮询任务
        private CancellationTokenSource? _watcherCts;     // 全局游戏流程监听
        private CancellationTokenSource? _champSelectCts; // 选人阶段专用轮询

        // 状态标志
        private bool _gameEndHandled = false; // 防止重复处理游戏结束
        private bool _hasAutoPreliminated = false; // 只保留这个标志，用于普通模式的预选（ARAM 不需要停止）
        private bool _hasSwappedInAram = false; // 恢复：ARAM 已抢过英雄标志（每局重置，一抢就停）

        private DateTime _lastMyTeamUpdate = DateTime.MinValue; // 防抖 + 快照判断
        private string _lastTeamStructureSnapshot = "";   // 新增：用于判断队伍结构是否真正变化
        private bool _hasAcceptedReadyCheck = false;   // 新增：本轮是否已接受

        public GameFlowWatcher(FormMain form, FormUiStateManager uiManager, PlayerCardManager cardManager, MatchQueryProcessor matchQueryProcessor)
        {
            _form = form;
            _uiManager = uiManager;
            _cardManager = cardManager;
            _matchQueryProcessor = matchQueryProcessor;
        }

        #region 1. 全局游戏流程监听（Lobby → Matchmaking → ChampSelect → InProgress → End）

        /// <summary>
        /// 启动后台轮询，监听游戏阶段变化（每秒检查一次）
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
                                return; // 退出任务
                            }

                            if (phase != lastPhase)
                            {
                                await HandleGameflowPhase(phase, lastPhase);
                                lastPhase = phase;
                            }

                            await Task.Delay(1000, token); // 正常延迟
                        }
                        catch (TaskCanceledException)
                        {
                            // 正常取消：窗体关闭或手动停止，静默退出
                            return;
                        }
                        catch (Exception ex)
                        {
                            // 真正的网络或其他异常，才记录日志
                            Debug.WriteLine($"[GameflowWatcher] 轮询内部异常：{ex}");
                            // 可选：短暂等待后继续，避免异常轰炸
                            await Task.Delay(2000, token);
                        }
                    }
                }, token);
            }
            catch (TaskCanceledException)
            {
                // 外部也可能收到取消（极少情况），静默忽略
                // 不打印任何日志
            }
            catch (Exception ex)
            {
                // 只有非取消的严重异常才记录
                Debug.WriteLine($"[GameflowWatcher] 严重异常：{ex}");
            }
        }


        /// <summary>
        /// 根据游戏阶段执行对应逻辑
        /// </summary>
        public async Task HandleGameflowPhase(string phase, string? previousPhase)
        {
            switch (phase)
            {
                case "Matchmaking":
                case "ReadyCheck":
                    _uiManager.IsGame = false;

                    // 开局时立刻清空所有战绩缓存（包括你自己）
                    _cardManager.ClearAllCaches();
                    _cardManager.ClearGameState();
                    _matchQueryProcessor.ClearPlayerMatchCache();

                    // 🔥 新增：自动接受对局
                    if (phase == "ReadyCheck" && _form.GetAppConfig()?.EnableAutoAcceptQueue == true)
                    {
                        await TryAutoAcceptQueueAsync();
                    }

                    FormUiStateManager.SafeInvoke(_form.imageTabControl1, () =>
                    {
                        _uiManager.SetLcuUiState(_uiManager.LcuReady, _uiManager.IsGame);
                        _form.imageTabControl1.SelectedIndex = 1;
                    });
                    break;
                // 在 HandleGameflowPhase 中：只负责“秒隐 + 强制重绘”，然后立即启动选人逻辑
                case "ChampSelect":
                    _uiManager.IsGame = true;

                    // 先清理提示面板
                    FormUiStateManager.SafeInvoke(_form, () =>
                    {
                        if (_form._waitingPanel != null)
                        {
                            // 使用 BeginInvoke 确保清理在下一个消息循环中完成
                            _form.BeginInvoke(new Action(() =>
                            {
                                if (_form.penalGameMatchData.Controls.Contains(_form._waitingPanel))
                                {
                                    _form.penalGameMatchData.Controls.Remove(_form._waitingPanel);
                                }
                                _form._waitingPanel.Dispose();
                                _form._waitingPanel = null;

                                // 立即重绘，避免残影
                                _form.penalGameMatchData.Invalidate();
                                _form.penalGameMatchData.Update();
                            }));
                        }

                        // 准备对战面板
                        _form.tableLayoutPanel1.Controls.Clear();
                        _form.tableLayoutPanel1.Visible = true;
                        _form.tableLayoutPanel1.Dock = DockStyle.Fill;
                        if (!_form.penalGameMatchData.Controls.Contains(_form.tableLayoutPanel1))
                        {
                            _form.penalGameMatchData.Controls.Add(_form.tableLayoutPanel1);
                        }
                    });

                    // 立即启动选人阶段详细逻辑（不等待）
                    _ = OnChampSelectStart();

                    break;

                case "InProgress":
                    _champSelectCts?.Cancel();  // 停止我方轮询
                    await ShowEnemyTeamCards(); // 显示敌方战绩卡片
                    break;

                case "EndOfGame":
                case "PreEndOfGame":
                case "WaitingForStats":
                case "Lobby":
                case "None":
                    _hasAcceptedReadyCheck = false; //是否接受对局
                    // 游戏结束或回到大厅时也清空缓存（双保险）
                    _cardManager.ClearAllCaches();
                    _cardManager.ClearGameState();
                    _matchQueryProcessor.ClearPlayerMatchCache();

                    await HandleGameEndPhase(previousPhase);
                    break;
            }
        }

        #endregion

        #region 2. 自动预选 / 抢英雄核心逻辑
        /// <summary>
        /// 自动预选功能核心方法，新增根据勾选的模式进行过滤
        /// </summary>
        private async Task TryAutoPreliminaryAsync()
        {
            // 先检查全局是否启用自动预选
            var preConfig = _form.GetAppConfig()?.Preliminary;
            if (preConfig == null || !preConfig.EnableAutoPreliminary)
                return;

            var preList = await _form.GetPreSelectedHeroesAsync();
            if (!preList.Any())
                return;

            var session = await Globals.lcuClient.GetChampSelectSession();
            if (session == null)
                return;

            int queueId = session["queueId"]?.Value<int>() ?? 0;

            // 根据 queueId 和用户配置决定是否执行
            bool shouldExecute = queueId switch
            {
                // 匹配模式：盲选(430)、征召(400)等
                400 or 430 => _form.GetAppConfig().EnablePreliminaryInNormal,

                // 排位模式：单双排(420)、灵活选排(440)
                420 or 440 => _form.GetAppConfig().EnablePreliminaryInRanked,

                // 大乱斗
                450 => _form.GetAppConfig().EnablePreliminaryInAram,

                // 海克斯大乱斗（Nexus Blitz / Hexakill ARAM）
                2400 => _form.GetAppConfig().EnablePreliminaryInNexusBlitz,

                _ => false // 其他模式一律不执行
            };

            if (!shouldExecute)
            {
                Debug.WriteLine($"[自动预选] 当前模式 queueId={queueId} 未勾选，跳过自动预选");
                return;
            }

            // 自动预选英雄，大乱斗与海克斯大乱斗
            if (queueId == 450 || queueId == 2400) // ARAM 类模式：一直抢最高优先级
            {
                await Globals.lcuClient.AutoSwapToHighestPriorityAsync(preList);
                return;
            }

            // 自动预选，普通匹配模式与排位
            if (_hasAutoPreliminated) return;

            bool success = await Globals.lcuClient.AutoDeclareIntentAsync(preList);
            if (success)
            {
                _hasAutoPreliminated = true;
                Debug.WriteLine("[自动预选] 普通模式意图声明成功");
            }
        }
        #endregion

        #region 自动接受对局
        /// <summary>
        /// 尝试自动接受匹配
        /// </summary>
        private async Task TryAutoAcceptQueueAsync()
        {
            if (_hasAcceptedReadyCheck)
                return;

            try
            {
                // 延迟一小段时间，让 Ready Check 弹窗完全弹出
                await Task.Delay(650);

                bool success = await Globals.lcuClient.AcceptReadyCheckAsync();

                if (success)
                {
                    _hasAcceptedReadyCheck = true;
                    Debug.WriteLine("[自动接受] ✅ 已成功接受对局");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动接受] 执行异常: {ex.Message}");
            }
        }
        #endregion

        #region 3. 选人阶段核心轮询（ChampSelect）
        /// <summary>
        /// 进入选人阶段时启动：高频轮询处理队伍显示 + 自动抢英雄
        /// </summary>
        private async Task OnChampSelectStart()
        {
            _champSelectCts?.Cancel();
            _champSelectCts = new CancellationTokenSource();
            var token = _champSelectCts.Token;
            Debug.WriteLine("进入选人阶段，已由 HandleGameflowPhase 完成界面切换");

            // 重置本局状态
            _gameEndHandled = false;
            _hasAutoPreliminated = false;
            _hasSwappedInAram = false; // 每局重置 ARAM 标志
            _hasAcceptedReadyCheck = false; //是否自动接受对局

            // 选人阶段轮询循环
            await Task.Run(async () =>
            {
                //await Task.Delay(800, token);  // 先等 800ms，让隐藏和重绘彻底完成

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var currentPhase = await Globals.lcuClient.GetGameflowPhase();
                        if (currentPhase != "ChampSelect") break;
                        await ShowMyTeamCards(); // 更新我方卡片 + 战绩
                        await TryAutoPreliminaryAsync(); // 自动预选或抢英雄

                        // 推荐：恢复高频轮询（随机模式下更快抢英雄）
                        var session = await Globals.lcuClient.GetChampSelectSession();
                        int queueId = session?["queueId"]?.Value<int>() ?? 0;
                        int delay = queueId switch
                        {
                            450 => 500,
                            2400 => 500,    // 2400的延迟设为500ms，你可以根据需要调整
                            _ => 1000       // 默认值
                        };
                        await Task.Delay(delay, token);
                    }
                    catch (TaskCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("选人阶段轮询异常: " + ex.Message);
                    }
                }
            }, token);
        }
        #endregion

        #region 4. 队伍卡片显示
        private async Task ShowMyTeamCards()
        {
            var session = await Globals.lcuClient.GetChampSelectSession();
            if (session == null) return;

            var myTeam = session["myTeam"] as JArray;
            if (myTeam == null || myTeam.Count == 0) return;

            int row = myTeam[0]?["team"]?.Value<int>() == 1 ? 0 : 1;

            // === 1. 生成两种快照 ===
            // 结构快照（用于判断是否需要重建卡片）
            var structureSnapshot = string.Join("|", myTeam.Select(p =>
                $"{p["summonerId"]?.ToString() ?? "0"}"));

            // 英雄快照（用于判断是否需要更新头像）
            var heroSnapshot = string.Join("|", myTeam.Select(p =>
                $"{p["summonerId"]?.ToString() ?? "0"}_{p["championId"]?.ToString() ?? "0"}"));

            bool structureChanged = structureSnapshot != _lastTeamStructureSnapshot;
            bool heroChanged = heroSnapshot != _form.lastChampSelectSnapshotString;

            if (!structureChanged && !heroChanged)
                return;

            _lastTeamStructureSnapshot = structureSnapshot;
            _form.lastChampSelectSnapshotString = heroSnapshot;

            // 🔥🔥🔥 关键修复：始终更新缓存 🔥🔥🔥
            _form._cachedMyTeam = myTeam;
            Debug.WriteLine($"[ShowMyTeamCards] 已更新 _cachedMyTeam，人数: {myTeam.Count}");

            _lastMyTeamUpdate = DateTime.Now;

            Debug.WriteLine($"[ShowMyTeamCards] Row={row} 更新触发 | 结构变化:{structureChanged} | 英雄变化:{heroChanged}");

            // === 2. 根据变化类型决定执行什么 ===
            if (structureChanged)
            {
                // 换人、有人进出 → 完整重建 + 刷新战绩
                await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: row);
                await _cardManager.FillPlayerMatchInfoAsync(myTeam, isMyTeam: true, row: row);
            }
            else if (heroChanged)
            {
                // 仅换英雄 → 只更新头像（不再调用Fill）
                await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: row);
                Debug.WriteLine($"[仅换英雄] Row={row} 只更新头像，不刷新战绩");
            }
        }

        private async Task ShowEnemyTeamCards()
        {
            try
            {
                // 获取当前玩家 puuid
                var currentSummoner = await Globals.lcuClient.GetCurrentSummoner();
                if (currentSummoner == null) return;

                string myPuuid = currentSummoner["puuid"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(myPuuid)) return;

                var sessionData = await Globals.lcuClient.GetGameSession();
                if (sessionData == null) return;

                // === 重要修复：正确获取 queueId ===
                int queueId = sessionData["gameData"]?["queue"]?["id"]?.Value<int>() ?? 0;
                Globals.CurrGameMod = queueId.ToString();

                Debug.WriteLine($"[ShowEnemyTeamCards] 当前模式 queueId = {queueId}");

                // 获取游戏会话数据（包含两队完整信息）
                var teamOne = sessionData["gameData"]?["teamOne"] as JArray;
                var teamTwo = sessionData["gameData"]?["teamTwo"] as JArray;

                if (teamOne == null || teamTwo == null) return;

                // 判断自己在哪一队，避免重复查询
                bool isInTeamOne = teamOne.Any(t => t["puuid"]?.ToString() == myPuuid);
                JArray enemyTeam = isInTeamOne ? teamTwo : teamOne;

                // 🔥🔥🔥 关键修复：更新敌方缓存 🔥🔥🔥
                _form._cachedEnemyTeam = enemyTeam;
                Debug.WriteLine($"[ShowEnemyTeamCards] 已更新 _cachedEnemyTeam，人数: {enemyTeam.Count}");

                // 选择敌方队伍和对应行号
                int enemyRow = isInTeamOne ? 1 : 0;

                _form._cachedEnemyTeam = enemyTeam;

                // 缓存并显示敌方卡片
                await _cardManager.CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);
                await _cardManager.FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow);

                // 卡片数据校验与补全
                await _cardManager.ValidateAndCompleteAllCards(teamOne, teamTwo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShowEnemyTeamCards] 异常: {ex.Message}");
            }
        }
        #endregion

        #region 5. 游戏结束与清理

        /// <summary>
        /// 游戏结束后清理缓存并重置UI
        /// </summary>
        private async Task OnGameEnd()
        {
            Debug.WriteLine("游戏已结束，正在清空缓存及队伍存储信息，重置UI");

            // 清理所有状态
            _cardManager.ClearAllCaches();
            _champSelectCts?.Cancel();
            _form.lastChampSelectSnapshot.Clear();

            // 🔥 确保只设置一次强制刷新
            if (!RefreshState.ForceMatchRefresh)
            {
                RefreshState.ForceMatchRefresh = true;
                Debug.WriteLine("[OnGameEnd] 设置 ForceMatchRefresh=true");
            }

            // 🔥 添加延迟，确保UI已准备好
            await Task.Delay(1000);

            _form.InvokeIfRequired(async () =>
            {
                try
                {
                    // 🔥 使用更安全的方式调用
                    await _form.InitializeDefaultTab();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OnGameEnd] 调用 InitializeDefaultTab 异常: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 游戏结束后的统一清理（只执行一次）
        /// </summary>
        private async Task HandleGameEndPhase(string? previousPhase)
        {
            if (!_gameEndHandled &&
                (previousPhase == "InProgress" || previousPhase == "WaitingForStats" || previousPhase == "ChampSelect"))
            {
                _gameEndHandled = true;
                await OnGameEnd();
            }
        }

        /// <summary>
        /// 停止所有后台轮询任务
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
        #endregion

        #region 6. 辅助方法

        /// <summary>
        /// LCU 断开连接时的处理
        /// </summary>
        private void OnLcuDisconnected()
        {
            _uiManager.LcuReady = false;
            _uiManager.IsGame = false;
            _watcherCts?.Cancel();
            _uiManager.SetLcuUiState(false, false);
            _form.StartLcuConnectPolling();
        }

        /// <summary>
        /// 外部调用：清理游戏相关状态（用于模式切换等）
        /// </summary>
        public void ClearGameState()
        {
            _form.lastChampSelectSnapshot.Clear();
            _cardManager.ClearGameState();
        }

        #endregion
    }
}