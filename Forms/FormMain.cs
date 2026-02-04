using League.Caching;
using League.Clients;
using League.Controls;
using League.Extensions;
using League.Infrastructure;
using League.Managers;
using League.Models;
using League.Parsers;
using League.PrimaryElection;
using League.Services;
using League.States;
using League.UIState;
using League.uitls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace League
{
    public partial class FormMain : Form
    {
        // 添加 WebSocket 管理器
        private LcuWebSocketManager? _webSocketManager;

        // 管理器实例
        private FormUiStateManager? _uiManager;
        private GameFlowWatcher? _gameFlowWatcher;
        private PlayerCardManager? _playerCardManager;
        private MatchQueryProcessor? _matchQueryProcessor;
        private ConfigUpdateManager? _configUpdateManager;
        private MatchDetailManager? _matchDetailManager;

        // 原有字段
        private AsyncPoller _lcuPoller = new AsyncPoller();
        private MatchTabContent? _matchTabContent;
        private CancellationTokenSource? _watcherCts;
        private CancellationTokenSource? _champSelectCts;
        public int myTeamId = 0;
        public List<string> lastChampSelectSnapshot = new List<string>();
        public string lastChampSelectSnapshotString = "";   // 新增：更可靠的快照字符串
        private Poller _tab1Poller = new Poller();
        private bool _champSelectMessageSent = false;
        public JArray? _cachedMyTeam;
        public JArray? _cachedEnemyTeam;
        private LeagueConfig? _appConfig;
        public LeagueConfig GetAppConfig() => _appConfig;

        // UI控件引用
        public Panel? _waitingPanel; // 添加这个字段

        // 读取预选英雄列表
        private List<PreliminaryHero>? _preSelectedHeroes = null;

        // 战绩发送处理
        private ChatMessageBuilder? _chatMessageBuilder;

        public FormMain()
        {
            InitializeComponent();
            InitializeManagers();
        }

        private void InitializeManagers()
        {
            // UI提示处理
            _uiManager = new FormUiStateManager(this);

            // 选人阶段卡片战绩查询
            _matchQueryProcessor = new MatchQueryProcessor();

            // 选人阶段卡片管理器
            _playerCardManager = new PlayerCardManager(this, _matchQueryProcessor);

            // 游戏流程监视器
            _gameFlowWatcher = new GameFlowWatcher(this, _uiManager, _playerCardManager, _matchQueryProcessor);

            _configUpdateManager = new ConfigUpdateManager(this);
            _matchDetailManager = new MatchDetailManager(this);

            // 🔥 正确初始化 ChatMessageBuilder
            _chatMessageBuilder = new ChatMessageBuilder(_playerCardManager.GetAllCachedPlayerInfos);
        }

        public void SaveAppConfig()
        {
            LeagueConfigManager.Save(_appConfig);
            Debug.WriteLine("[配置] 主窗体保存配置完成");
        }

        #region 窗体事件
        private async void FormMain_Load(object sender, EventArgs e)
        {
            try
            {
                // 检查更新（不等待，避免阻塞UI）
                _ = _configUpdateManager.CheckForUpdates();

                // 加载图片
                var img1 = Image.FromFile(AppDomain.CurrentDomain.BaseDirectory + @"\Assets\Defaults\01.png");
                var img2 = Image.FromFile(AppDomain.CurrentDomain.BaseDirectory + @"\Assets\Defaults\02.png");
                var img3 = Image.FromFile(AppDomain.CurrentDomain.BaseDirectory + @"\Assets\Defaults\03.png");

                imageTabControl1.TabPages[0].Tag = img1;
                imageTabControl1.TabPages[1].Tag = img2;
                imageTabControl1.TabPages[2].Tag = img3;

                // 绑定事件
                imageTabControl1.MouseMove += (s, ev) =>
                    _uiManager!.HandleImageTabControlMouseMove(s, ev);

                // 读取配置
                _appConfig = _configUpdateManager!.LoadConfig();

                // 根据配置恢复复选框
                checkBoxFilterMode.Checked = _appConfig.FilterByGameMode;
                // 恢复复选框状态（新增）
                chkNormal.Checked = _appConfig.EnablePreliminaryInNormal;   //匹配
                chkRanked.Checked = _appConfig.EnablePreliminaryInRanked;   //排位
                chkAram.Checked = _appConfig.EnablePreliminaryInAram;       //大乱斗
                chkNexus.Checked = _appConfig.EnablePreliminaryInNexusBlitz;    //海克斯乱斗
                chkAutoAccept.Checked = _appConfig.EnableAutoAcceptQueue;   // 在恢复其他复选框的位置添加

                // 根据配置文件恢复消息发送信息 ===
                if (rbModeMatch != null && rbModeCustom != null && txtCustomContent != null)
                {
                    // 1. 恢复文本框里的内容
                    txtCustomContent.Text = _appConfig.CustomSendContent ?? string.Empty;

                    // 2. 依据配置勾选对应的单选框
                    if (_appConfig.SendMode == 2)
                    {
                        rbModeCustom.Checked = true;
                        txtCustomContent.Visible = true; // 显示文本框
                    }
                    else
                    {
                        rbModeMatch.Checked = true;
                        txtCustomContent.Visible = false; // 隐藏文本框
                    }

                    // 3. 恢复后再绑定事件，防止初始化触发不必要的自动保存
                    rbModeMatch.CheckedChanged += SendMode_CheckedChanged;
                    rbModeCustom.CheckedChanged += SendMode_CheckedChanged;

                    // 绑定文本框失去焦点事件，用来自动保存文本
                    txtCustomContent.Leave += TxtCustomContent_Leave;
                }

                // 绑定事件（建议统一一个事件处理）
                chkNormal.CheckedChanged += ModeCheckBox_CheckedChanged;
                chkRanked.CheckedChanged += ModeCheckBox_CheckedChanged;
                chkAram.CheckedChanged += ModeCheckBox_CheckedChanged;
                chkNexus.CheckedChanged += ModeCheckBox_CheckedChanged;
                chkAutoAccept.CheckedChanged += AutoAccept_CheckedChanged;

                // 安装热键钩子
                InstallKeyboardHook();

                // 启动轮询 LCU 检测
                StartLcuConnectPolling();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[全局初始化异常] {ex.Message}");
            }
        }
        #endregion

        #region LCU连接管理
        /// <summary>
        /// 启动窗口，轮询监听是否登录了lcu客户端
        /// </summary>
        public void StartLcuConnectPolling()
        {
            _uiManager!.SetLcuUiState(connected: false, inGame: false);

            _lcuPoller.Start(async delegate
            {
                if (!_uiManager!.LcuReady)
                {
                    if (await Globals.lcuClient.InitializeAsync())
                    {
                        _uiManager!.LcuReady = true;
                        _lcuPoller.Stop();

                        // 在API连接成功后初始化WebSocket管理器
                        _webSocketManager = new LcuWebSocketManager(Globals.lcuClient);
                        bool wsInitialized = await _webSocketManager.InitializeAsync();

                        Debug.WriteLine($"[WebSocket] 初始化{(wsInitialized ? "成功" : "失败")}");

                        // 初始化英雄资源，及查询SGP服务
                        await InitializeAfterLcuConnected();
                    }
                    else
                    {
                        Debug.WriteLine("[LCU检测中] 未找到 LCU 客户端");
                    }
                }
            }, 5000);
        }

        private async Task InitializeAfterLcuConnected()
        {
            // 加载资源
            Globals.resLoading.loadingResource(Globals.lcuClient);

            // 初始化SGP
            if (await Globals.sgpClient.InitSgpAsync(Globals.lcuClient.Client))
            {
                Debug.WriteLine("SGP 连接初始化成功！");
            }
            else
            {
                Debug.WriteLine("SGP 连接初始化失败！");
            }

            // 初始化战绩Tab
            InitializeMatchTabContent();

            // 初始化默认Tab和预热
            await this.InvokeIfRequiredAsync(async () =>
            {
                RefreshState.ForceMatchRefresh = false;
                await InitializeDefaultTab();
                _configUpdateManager!.PreWarmUiComponents();

                // 开始监听游戏流程
                string? currentPhase = await Globals.lcuClient.GetGameflowPhase();
                if (!string.IsNullOrEmpty(currentPhase))
                {
                    await _gameFlowWatcher!.HandleGameflowPhase(currentPhase, null);
                }
                _gameFlowWatcher!.StartGameflowWatcher();
            });

            _uiManager!.SetLcuUiState(_uiManager!.LcuReady, _uiManager!.IsGame);
        }

        private void InitializeMatchTabContent()
        {
            FormUiStateManager.SafeInvoke(panelMatchList, delegate
            {
                panelMatchList.Controls.Clear();
                _matchTabContent = new MatchTabContent();
                _matchTabContent.Dock = DockStyle.Fill;
                panelMatchList.Controls.Add(_matchTabContent);
            });
        }
        #endregion

        #region 热键监听 - 低级键盘钩子版

        // 低级键盘钩子相关字段
        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc;
        private DateTime _lastHookTrigger = DateTime.MinValue;
        private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(800);

        // 委托定义
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        // Windows API 声明
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        // 安装钩子（在 FormMain_Load 里调用）
        private void InstallKeyboardHook()
        {
            _proc = HookCallback;
            using var curModule = Process.GetCurrentProcess().MainModule!;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);

            if (_hookId == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[钩子] 安装低级键盘钩子失败，错误码: {error}");
            }
            else
            {
                Debug.WriteLine("[钩子] 低级键盘钩子安装成功");
            }
        }

        // 卸载钩子（在 OnFormClosing 里调用）
        private void UninstallKeyboardHook()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                Debug.WriteLine("[钩子] 低级键盘钩子已卸载");
            }
        }

        

        // 低级键盘钩子回调（核心） - 已加异常保护
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            try
            {
                if (wParam == (IntPtr)WM_KEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    Keys key = (Keys)vkCode;

                    bool isCtrlDown = (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;

                    bool isTargetKey = (key == Keys.F9 || key == Keys.F11 || key == Keys.F12 ||
                                       (isCtrlDown && key == Keys.F7));

                    if (isTargetKey)
                    {
                        DateTime now = DateTime.Now;
                        if ((now - _lastHookTrigger) < _debounceInterval)
                        {
                            return (IntPtr)1;
                        }
                        _lastHookTrigger = now;

                        Debug.WriteLine($"[钩子触发] {key} 被按下 (Ctrl={isCtrlDown})");

                        // 异步处理
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                string phase = await GetGameflowPhaseSafe();

                                Debug.WriteLine($"[热键处理] 当前阶段: {phase}");

                                if (phase == "InProgress")
                                {
                                    if (key == Keys.F9 || key == Keys.F11)
                                        await HandleMyTeam();
                                    else if (key == Keys.F12)
                                        await HandleFullTeam();
                                }
                                else if (phase == "ChampSelect" && isCtrlDown && key == Keys.F7)
                                {
                                    await HandleChampSelectF7();
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[热键执行异常] {ex.Message}\n{ex.StackTrace}");
                            }
                        });

                        return (IntPtr)1; // 吞掉按键
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HookCallback 顶层异常] {ex.Message}");
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
        #endregion

        #region 热键监听

        // 🔥 选人阶段发送（Ctrl+F7）
        private async Task HandleChampSelectF7()
        {
            string phase = await GetGameflowPhaseSafe();
            if (phase != "ChampSelect")
            {
                Debug.WriteLine($"[Ctrl+F7] 当前不是选人阶段: {phase}");
                return;
            }

            // --- 线程安全地获取 UI 状态 ---
            bool isCustomMode = false;
            string msg = "";
            this.Invoke(() =>
            {
                isCustomMode = rbModeCustom?.Checked ?? false;
                msg = txtCustomContent?.Text ?? "";
            });

            // 如果是战绩模式，走原有逻辑
            if (!isCustomMode)
            {
                var myTeam = _cachedMyTeam;
                if (myTeam == null || myTeam.Count == 0)
                {
                    Debug.WriteLine("[Ctrl+F7] 缓存为空，尝试从Session实时获取");
                    var session = await Globals.lcuClient.GetChampSelectSession();
                    if (session != null) _cachedMyTeam = session["myTeam"] as JArray;
                    myTeam = _cachedMyTeam;
                }

                if (myTeam == null || myTeam.Count == 0)
                {
                    Debug.WriteLine("[Ctrl+F7] 仍无法获取我方队伍数据");
                    return;
                }

                msg = _chatMessageBuilder!.BuildMyTeamSummary(myTeam);
            }

            if (string.IsNullOrWhiteSpace(msg))
            {
                Debug.WriteLine("[Ctrl+F7] 发送的消息为空");
                return;
            }

            bool success = await Globals.lcuClient.SendChampSelectMessageAsync(msg);
            Debug.WriteLine($"[Ctrl+F7] 发送 {(success ? "成功" : "失败")}");
        }

        private bool _isSendingMessage = false;

        // 🔥 我方队伍发送（F9 / F11）
        private async Task HandleMyTeam()
        {
            if (_isSendingMessage) return;

            string phase = await GetGameflowPhaseSafe();
            if (phase != "InProgress")
            {
                Debug.WriteLine($"[F9/F11] 非游戏阶段: {phase}");
                return;
            }

            // --- 线程安全地获取 UI 状态 ---
            bool isCustomMode = false;
            string msg = "";
            this.Invoke(() =>
            {
                isCustomMode = rbModeCustom?.Checked ?? false;
                msg = txtCustomContent?.Text ?? "";
            });

            // 如果是战绩模式，走原有逻辑
            if (!isCustomMode)
            {
                var myTeam = _cachedMyTeam;
                if (myTeam == null || myTeam.Count == 0)
                {
                    Debug.WriteLine("[F9/F11] 缓存为空，尝试从Session获取");
                    var session = await Globals.lcuClient.GetChampSelectSession();
                    if (session != null) _cachedMyTeam = session["myTeam"] as JArray;
                    myTeam = _cachedMyTeam;
                }

                if (myTeam == null || myTeam.Count == 0)
                {
                    Debug.WriteLine("[F9/F11] 无法获取我方队伍");
                    return;
                }

                msg = _chatMessageBuilder!.BuildMyTeamSummary(myTeam);
            }

            if (string.IsNullOrWhiteSpace(msg)) return;

            try
            {
                _isSendingMessage = true;
                bool success = await Globals.lcuClient.SendInGameMessageAsync(msg);
                Debug.WriteLine($"[F9/F11] 发送结果: {(success ? "成功" : "失败")}");
            }
            finally
            {
                _isSendingMessage = false;
            }
        }

        // 🔥 全队发送（F12）
        private async Task HandleFullTeam()
        {
            string phase = await GetGameflowPhaseSafe();
            if (phase != "InProgress")
            {
                Debug.WriteLine($"[F12] 非游戏阶段: {phase}");
                return;
            }

            // --- 线程安全地获取 UI 状态 ---
            bool isCustomMode = false;
            string msg = "";
            this.Invoke(() =>
            {
                isCustomMode = rbModeCustom?.Checked ?? false;
                msg = txtCustomContent?.Text ?? "";
            });

            // 如果是战绩模式，走原有逻辑
            if (!isCustomMode)
            {
                var myTeam = _cachedMyTeam;
                var enemyTeam = _cachedEnemyTeam;

                if (myTeam?.Count == 0 || enemyTeam?.Count == 0)
                {
                    Debug.WriteLine("[F12] 缓存为空，尝试从GameSession获取");
                    var session = await Globals.lcuClient.GetGameSession();
                    if (session != null)
                    {
                        myTeam = session["gameData"]?["teamOne"] as JArray;
                        enemyTeam = session["gameData"]?["teamTwo"] as JArray;
                        _cachedMyTeam = myTeam;
                        _cachedEnemyTeam = enemyTeam;
                    }
                }

                if (myTeam?.Count > 0 == false || enemyTeam?.Count > 0 == false)
                {
                    Debug.WriteLine("[F12] 仍无法获取队伍数据");
                    return;
                }

                msg = _chatMessageBuilder!.BuildFullTeamSummary(myTeam, enemyTeam);
            }

            if (string.IsNullOrWhiteSpace(msg)) return;

            bool success = await Globals.lcuClient.SendInGameMessageAsync(msg);
            Debug.WriteLine($"[F12] 发送结果: {(success ? "成功" : "失败")}");
        }

        // 🔥 安全获取游戏阶段（3秒超时）
        private async Task<string> GetGameflowPhaseSafe()
        {
            try
            {
                return await Task.Run(async () => await Globals.lcuClient.GetGameflowPhase())
                    .WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                return "Unknown";
            }
        }
        #endregion

        #region 战绩查询
        /// <summary>
        /// 默认查询当前客户端玩家对战数据
        /// </summary>
        public async Task InitializeDefaultTab()
        {
            Debug.WriteLine($"[InitializeDefaultTab] 开始，ForceMatchRefresh={RefreshState.ForceMatchRefresh}");

            var summoner = await Globals.lcuClient.GetCurrentSummoner();
            if (summoner == null)
            {
                Debug.WriteLine("[InitializeDefaultTab] 获取当前召唤师失败");
                return;
            }

            Globals.CurrentPuuid = summoner["puuid"]?.ToString() ?? "";
            Globals.CurrentSummonerName = summoner["gameName"]?.ToString();

            Debug.WriteLine($"[InitializeDefaultTab] 当前玩家 PUUID: {Globals.CurrentPuuid}");

            var rankedStats = await GetRankedStatsAsync(summoner["puuid"]?.ToString() ?? "");
            string privacyStatus = GetPrivacyStatus(summoner);

            // 记录调用 CreateNewTab 前的状态
            Debug.WriteLine($"[InitializeDefaultTab] 调用 CreateNewTab，ForceMatchRefresh={RefreshState.ForceMatchRefresh}");

            _matchTabContent?.CreateNewTab(
                summoner["summonerId"]?.ToString() ?? "",
                summoner["gameName"]?.ToString() ?? "",
                summoner["tagLine"]?.ToString() ?? "",
                summoner["puuid"]?.ToString() ?? "",
                summoner["profileIconId"]?.ToString() ?? "",
                summoner["summonerLevel"]?.ToString() ?? "",
                privacyStatus,
                rankedStats
            );

            Debug.WriteLine("[InitializeDefaultTab] 完成");
        }

        private async void btn_search_Click(object sender, EventArgs e)
        {
            btn_search.Enabled = false;
            btn_search.Text = "查询中...";
            _uiManager!.ShowLoadingIndicator();

            try
            {
                if (!_uiManager!.LcuReady)
                {
                    MessageBox.Show("LCU 客户端未连接，请先登录游戏并稍后重试！");
                    return;
                }

                string input = txtGameName.Text.Trim();
                if (!input.Contains("#"))
                {
                    MessageBox.Show("请输入完整名称，如：玩家名#区号");
                    return;
                }

                await SearchPlayerAsync(input);
            }
            catch (Exception ex)
            {
                MessageBox.Show("查询失败: " + ex.Message);
            }
            finally
            {
                _uiManager!.HideLoadingIndicator();
                btn_search.Enabled = true;
                btn_search.Text = "查询";
            }
        }

        private async Task SearchPlayerAsync(string playerName)
        {
            JObject? summoner = await Globals.lcuClient.GetSummonerByNameAsync(playerName);
            if (summoner == null)
            {
                MessageBox.Show("玩家不存在,本软件只能查询相同大区玩家!");
                return;
            }

            RefreshState.ForceMatchRefresh = true;
            var rankedStats = await GetRankedStatsAsync(summoner["puuid"]?.ToString() ?? "");
            string privacyStatus = GetPrivacyStatus(summoner);

            // 检查是否是当前玩家
            string summonerPuuid = summoner["puuid"]?.ToString() ?? "";
            bool isCurrentPlayer = string.Equals(summonerPuuid, Globals.CurrentPuuid, StringComparison.OrdinalIgnoreCase);

            // 如果是当前玩家，提示用户
            if (isCurrentPlayer)
            {
                Debug.WriteLine("[查询] 查询的是当前玩家，将刷新数据");
            }

            _matchTabContent?.CreateNewTab(
                summoner["summonerId"]?.ToString() ?? "",
                summoner["gameName"]?.ToString() ?? "",
                summoner["tagLine"]?.ToString() ?? "",
                summonerPuuid,
                summoner["profileIconId"]?.ToString() ?? "",
                summoner["summonerLevel"]?.ToString() ?? "",
                privacyStatus,
                rankedStats
            );
        }

        private async void Panel_PlayerIconClicked(string summonerId)
        {
            _uiManager!.ShowLoadingIndicator();
            try
            {
                JObject? summoner = await Globals.lcuClient.GetGameNameBySummonerId(summonerId);
                if (summoner != null)
                {
                    RefreshState.ForceMatchRefresh = true;
                    var rankedStats = await GetRankedStatsAsync(summoner["puuid"]?.ToString() ?? "");
                    string privacyStatus = GetPrivacyStatus(summoner);

                    _matchTabContent?.CreateNewTab(
                        summoner["summonerId"]?.ToString() ?? "",
                        summoner["gameName"]?.ToString() ?? "",
                        summoner["tagLine"]?.ToString() ?? "",
                        summoner["puuid"]?.ToString() ?? "",
                        summoner["profileIconId"]?.ToString() ?? "",
                        summoner["summonerLevel"]?.ToString() ?? "",
                        privacyStatus,
                        rankedStats
                    );
                    txtGameName.Text = "";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("查询失败: " + ex.Message);
            }
            finally
            {
                _uiManager!.HideLoadingIndicator();
            }
        }

        /// <summary>
        /// 查询数据（保留原有方法）
        /// </summary>
        public async Task<JArray?> LoadFullMatchDataAsync(string puuid, int begIndex, int pageSize, string? queueId = null, bool? forceRefresh = null)
        {
            bool refreshFlag = forceRefresh ?? RefreshState.ForceMatchRefresh;

            if (refreshFlag)
                //Debug.WriteLine("⚡ 强制刷新已启用");

            // 1️⃣ 尝试从缓存读取
            if (!refreshFlag)
            {
                var cached = MatchCache.Get(puuid, queueId, begIndex, pageSize);
                if (cached != null)
                {
                    //Debug.WriteLine($"从缓存读取: {puuid} [{queueId ?? "all"}] 起点{begIndex} 每页{pageSize}");
                    return cached;
                }
            }

            // 2️⃣ 发起网络请求
            var allGames = await Globals.sgpClient.SgpFetchLatestMatches(puuid, begIndex, pageSize, queueId);

            // 3️⃣ 写入缓存
            MatchCache.Add(puuid, queueId, begIndex, pageSize, allGames);

            // 4️⃣ 重置强制刷新状态（只生效一次）
            RefreshState.ForceMatchRefresh = false;

            return allGames;
        }

        /// <summary>
        /// 数据解析（保留原有方法）
        /// </summary>
        public async Task<Panel?> ParseGameToPanelAsync(JObject? gameObj, string summonerId, string gameName, string tagLine, int index)
        {
            if (gameObj == null) return null;

            MatchParserSgp _parser = new MatchParserSgp();
            _parser.PlayerIconClicked += Panel_PlayerIconClicked;
            Panel? panel = await _parser.ParseGameToPanelFromSgpAsync(gameObj, summonerId, gameName, tagLine, index);

            if (panel is MatchListPanel matchPanel)
            {
                matchPanel.DetailsClicked += delegate (MatchInfo matchInfo)
                {
                    try
                    {
                        //Debug.WriteLine("详情按钮被点击，模式：" + matchInfo.QueueId + "，开始显示详情");
                        if (_matchDetailManager!.IsSummonersRiftOrAram(matchInfo.QueueId))
                        {
                            _matchDetailManager!.ShowMatchDetails(matchInfo);
                        }
                        else
                        {
                            MessageBox.Show("该模式不支持查看详情！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Question);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("详情按钮点击异常: " + ex.Message);
                        MessageBox.Show("打开详情失败: " + ex.Message);
                    }
                };
                matchPanel.ReplayClicked += async delegate (MatchInfo matchInfo)
                {
                    await _matchDetailManager!.HandleReplayClick(matchInfo, matchPanel);
                };
            }
            return panel;
        }

        #endregion

        #region 辅助方法
        private async Task<Dictionary<string, RankedStats>> GetRankedStatsAsync(string puuid)
        {
            if (string.IsNullOrEmpty(puuid))
                return new Dictionary<string, RankedStats>();

            var rankedJson = await Globals.lcuClient.GetCurrentRankedStatsAsync(puuid);
            return RankedStats.FromJson(rankedJson) ?? new Dictionary<string, RankedStats>();
        }

        private string GetPrivacyStatus(JObject summoner)
        {
            return summoner["privacy"]?.ToString().Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) ?? false
                ? "公开" : "隐藏";
        }

        // 为消息发送提供当前Puuid
        public string GetCurrentPuuid()
        {
            return Globals.CurrentPuuid;
        }

        /// <summary>
        /// 读取预选列表的方法（异步版本，避免阻塞UI线程）
        /// </summary>
        public async Task<List<PreliminaryHero>> GetPreSelectedHeroesAsync()
        {
            // 先检查缓存
            if (_preSelectedHeroes != null)
            {
                //Debug.WriteLine($"[自动预选] 使用缓存的预选列表，共 {_preSelectedHeroes.Count} 个英雄");
                return _preSelectedHeroes;
            }

            try
            {
                var configPath = "LeagueConfig.json";
                if (!File.Exists(configPath))
                {
                    //Debug.WriteLine("[自动预选] 未找到 LeagueConfig.json 文件（程序所在目录）");
                    _preSelectedHeroes = new List<PreliminaryHero>();
                    return _preSelectedHeroes;
                }

                // 改为异步读取，避免阻塞UI线程
                var json = await File.ReadAllTextAsync(configPath);

                var config = JsonConvert.DeserializeObject<LeagueConfig>(json);
                if (config?.Preliminary == null || config.Preliminary.Heroes == null || config.Preliminary.Heroes.Count == 0)
                {
                    //Debug.WriteLine("[自动预选] 配置中 Preliminary 或 Heroes 为空");
                    _preSelectedHeroes = new List<PreliminaryHero>();
                    return _preSelectedHeroes;
                }

                if (!config.Preliminary.EnableAutoPreliminary)
                {
                    //Debug.WriteLine("[自动预选] 配置中 EnableAutoPreliminary = false，已禁用自动预选");
                    _preSelectedHeroes = new List<PreliminaryHero>();
                    return _preSelectedHeroes;
                }

                _preSelectedHeroes = config.Preliminary.Heroes
                    .OrderBy(h => h.Priority)
                    .ToList();

                //Debug.WriteLine($"[自动预选] 成功加载预选列表，共 {_preSelectedHeroes.Count} 个英雄");
                return _preSelectedHeroes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动预选] 读取配置异常: {ex.Message}\n{ex.StackTrace}");
                _preSelectedHeroes = new List<PreliminaryHero>();
                return _preSelectedHeroes;
            }
        }
        #endregion

        #region 其他事件处理
        private void imageTabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            _uiManager!.HandleTabSelectionChanged(imageTabControl1.SelectedIndex, _tab1Poller);
        }

        private void lkbPreliminary_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (!_uiManager!.LcuReady)
            {
                MessageBox.Show("请先登录英雄联盟客户端并等待连接成功！", "客户端未连接", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (Preliminary pre = new Preliminary(_appConfig!))  // 传入主配置实例
            {
                pre.Owner = this;  // 重要！这样 Preliminary_FormClosing 里能找到 mainForm
                pre.ShowDialog();
            }

            _preSelectedHeroes = null;  // 清除缓存，下次自动预选会重新读取最新
            Debug.WriteLine("[自动预选] 预选配置已修改，缓存已清除");
        }

        private void checkBoxFilterMode_CheckedChanged(object sender, EventArgs e)
        {
            // 更新内存中的配置
            _appConfig!.FilterByGameMode = checkBoxFilterMode.Checked;

            // 直接保存到文件
            LeagueConfigManager.Save(_appConfig);

            // 通知MatchQueryProcessor更新筛选模式
            _matchQueryProcessor?.SetFilterMode(checkBoxFilterMode.Checked);

            Debug.WriteLine($"[配置] 过滤模式改为: {checkBoxFilterMode.Checked}，配置已保存");
        }

        private void ModeCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (_appConfig == null) return;

            _appConfig.EnablePreliminaryInNormal = chkNormal.Checked;
            _appConfig.EnablePreliminaryInRanked = chkRanked.Checked;
            _appConfig.EnablePreliminaryInAram = chkAram.Checked;
            _appConfig.EnablePreliminaryInNexusBlitz = chkNexus.Checked;

            SaveAppConfig(); // 立即保存到 LeagueConfig.json

            Debug.WriteLine("[自动预选模式] 配置已更新并保存");
        }

        private void AutoAccept_CheckedChanged(object sender, EventArgs e)
        {
            if (_appConfig == null) return;
            _appConfig.EnableAutoAcceptQueue = chkAutoAccept.Checked;

            SaveAppConfig();

            Debug.WriteLine($"[自动接受对局] 已更新配置: {_appConfig.EnableAutoAcceptQueue}");
        }

        // 当用户切换单选按钮时触发
        private void SendMode_CheckedChanged(object sender, EventArgs e)
        {
            if (rbModeCustom == null || txtCustomContent == null || _appConfig == null) return;

            // 联动控制文本框的显示与隐藏
            txtCustomContent.Visible = rbModeCustom.Checked;

            // 更新配置对象：如果是勾选了自定义，存2，否则存1
            _appConfig.SendMode = rbModeCustom.Checked ? 2 : 1;

            // 顺便联动切换文本框的显示隐藏
            txtCustomContent.Visible = rbModeCustom.Checked;

            // 存盘
            SaveAppConfig();
        }

        // 当多行文本框失去焦点（鼠标点击别处或切换窗口）时触发保存
        private void TxtCustomContent_Leave(object sender, EventArgs e)
        {
            if (txtCustomContent == null || _appConfig == null) return;

            // 将当前文本框的内容同步到配置中
            _appConfig.CustomSendContent = txtCustomContent.Text;

            // 存盘
            SaveAppConfig();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_matchTabContent != null)
            {
                _matchTabContent.CleanupAllTabs();
            }

            _gameFlowWatcher?.StopGameflowWatcher();
            _lcuPoller?.Stop();
            _tab1Poller?.Stop();

            // 清理WebSocket管理器
            _webSocketManager?.Dispose();

            // 卸载热键钩子
            UninstallKeyboardHook();

            base.OnFormClosing(e);

            _playerCardManager._uiLock?.Dispose();  // 如果你在 FormMain 持有引用的话
        }
        #endregion

        #region 静态全局类
        public static class Globals
        {
            public static LcuSession lcuClient = new LcuSession();
            public static SgpSession sgpClient = new SgpSession();
            public static ResourceLoading resLoading = new ResourceLoading();
            public static string? CurrentSummonerId;
            public static string? CurrentPuuid;
            public static string? CurrGameMod;
            public static string? CurrentSummonerName; // 纯 displayName（不带 #）

            // 新增这行：全局保存 WebSocket 客户端
            //public static LcuWebSocketClient? WsClient;
        }
        #endregion

    }
}