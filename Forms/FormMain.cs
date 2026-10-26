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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace League
{
    public partial class FormMain : Form
    {

        // 管理器实例
        private FormUiStateManager? _uiManager;
        private GameFlowWatcher? _gameFlowWatcher;
        public PlayerCardManager? _playerCardManager;
        private MatchQueryProcessor? _matchQueryProcessor;
        private ConfigUpdateManager? _configUpdateManager;
        private MatchDetailManager? _matchDetailManager;
        // 英雄管理器（用于预加载所有英雄数据）
        private ChampionManager? _championManager;
        private ClientUtilityService? _clientUtility;   //客户端辅助
        public StatusBarManager? _statusBar;

        // 原有字段
        private AsyncPoller _lcuPoller = new AsyncPoller();
        private MatchTabContent? _matchTabContent;
        public int myTeamId = 0;
        public List<string> lastChampSelectSnapshot = new List<string>();
        public string lastChampSelectSnapshotString = "";
        private Poller _tab1Poller = new Poller();
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

        // 热键注册及按键监听
        private HotkeyManager? _hotkeyManager;

        // 热键业务处理管理器
        private GameChatSender? _chatSender;

        public FormMain()
        {
            InitializeComponent();
            InitializeManagers();
        }

        private void InitializeManagers()
        {
            // UI提示处理
            _uiManager = new FormUiStateManager(this);

            // 预选英雄管理器
            _championManager = new ChampionManager(
                new HttpClient(),           // 直接新建，不依赖 LCU
                Globals.resLoading
            );

            // 选人阶段卡片战绩查询
            _matchQueryProcessor = new MatchQueryProcessor();

            // 选人阶段卡片管理器
            _playerCardManager = new PlayerCardManager(this, _matchQueryProcessor);

            // 游戏流程监视器
            _gameFlowWatcher = new GameFlowWatcher(this, _uiManager, _playerCardManager, _matchQueryProcessor);

            _configUpdateManager = new ConfigUpdateManager(this);
            _matchDetailManager = new MatchDetailManager(this);

            // 初始化按键消息预处理
            //_chatMessageBuilder = new ChatMessageBuilder(_playerCardManager.GetAllCachedPlayerInfos, null!);
            // ★★★ 修改为先传 null ★★★
            _chatMessageBuilder = null;  // 或者 new ChatMessageBuilder(..., null!) 但建议直接 null

            // 热键管理
            _hotkeyManager = new HotkeyManager(this);

            // 新增：业务处理管理器
            _chatSender = new GameChatSender(this, _chatMessageBuilder);

            _clientUtility = new ClientUtilityService();    // 初始化客户端辅助

            // 订阅热键
            _hotkeyManager.OnMyTeamHotkey += async () => await _chatSender.HandleMyTeamAsync();
            _hotkeyManager.OnFullTeamHotkey += async () => await _chatSender.HandleFullTeamAsync();
            _hotkeyManager.OnChampSelectF7 += async () => await _chatSender.HandleChampSelectF7Async();

            // 状态栏（Designer 里已有 statusStrip1、两个 Label）
            _statusBar = new StatusBarManager(
                this,
                toolStripStatusLabelConnection,
                toolStripStatusLabelTip
            );
        }

        public void SaveAppConfig()
        {
            LeagueConfigManager.Save(_appConfig);
            _statusBar?.TipConfigSaved();   // 所有保存都会提示「配置已保存」
            //Debug.WriteLine("[配置] 主窗体保存配置完成");
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
                checkBoxFilterMode.Checked = _appConfig.FilterByGameMode;   //是否在卡片中过滤模式

                // 恢复卡片战绩要查询的场数
                int count = _appConfig.CardMatchHistoryCount;
                cboCardMatchCount.SelectedIndex = count switch
                {
                    10 => 0,    // 第 0 项 = 10
                    30 => 2,    // 第 2 项 = 30（注释写成「第2项」容易误会）
                    50 => 3,
                    _ => 1      // 实际是 index==1 → 20，不是「第4项」
                };

                _matchQueryProcessor?.SetFilterMode(_appConfig.FilterByGameMode);
                _matchQueryProcessor?.SetMatchHistoryCount(_appConfig.CardMatchHistoryCount);

                chkNormal.Checked = _appConfig.EnablePreliminaryInNormal;   // 匹配
                chkRanked.Checked = _appConfig.EnablePreliminaryInRanked;   // 排位
                chkAram.Checked = _appConfig.EnablePreliminaryInAram;       // 大乱斗
                chkNexus.Checked = _appConfig.EnablePreliminaryInNexusBlitz;    // 海克斯乱斗
                chkAutoAccept.Checked = _appConfig.EnableAutoAcceptQueue;   // 自动接受对局
                chkUrf.Checked = _appConfig.EnablePreliminaryInUrf;             // 无限乱斗（900）
                chkPickUrf.Checked = _appConfig.EnablePreliminaryInPickUrf;     // 无限火力（1900）
                chkClassic.Checked = _appConfig.EnablePreliminaryInClassic; // 经典模式（4310/4320）
                chkBot.Checked = _appConfig.EnablePreliminaryInBot;

                // 恢复自动接受对局延时配置
                cboAutoAcceptDelay.SelectedIndex = _appConfig.AutoAcceptDelaySeconds switch
                {
                    5 => 1,     // 配置是5秒 → 选中第1项
                    10 => 2,    // 配置是10秒 → 选中第2项
                    _ => 0      // 0秒 或 其他值 → 默认选中第0项（立即）
                };
                chkHideSelf.Checked = _appConfig.HideSelfWhenSending;   //发送战绩时是否隐藏自己
                chkUseChampionName.Checked = _appConfig.UseChampionNameWhenSending; //发送战绩时是否使用英雄名称
                chkSkipHonor.Checked = _appConfig.EnableSkipHonor;  //自动跳过点赞界面
                chkSkipEndOfGameStats.Checked = _appConfig.EnableSkipEndOfGameStats;    //自动跳过结算统计界面
                chkAutoSkinSelect.Checked = _appConfig.EnableAutoSkinSelect;    // 是否自动配置皮肤

                // 消息发送恢复快捷键显示
                if (txtHotkeyChampSelect != null && txtHotkeyMyTeam != null && txtHotkeyFullTeam != null)
                {
                    txtHotkeyChampSelect.ReadOnly = true;
                    txtHotkeyMyTeam.ReadOnly = true;
                    txtHotkeyFullTeam.ReadOnly = true;

                    txtHotkeyChampSelect.Text = string.IsNullOrWhiteSpace(_appConfig.HotkeyChampSelect)
                        ? "Ctrl+F7" : _appConfig.HotkeyChampSelect;
                    txtHotkeyMyTeam.Text = string.IsNullOrWhiteSpace(_appConfig.HotkeyMyTeam)
                        ? "F9" : _appConfig.HotkeyMyTeam;
                    txtHotkeyFullTeam.Text = string.IsNullOrWhiteSpace(_appConfig.HotkeyFullTeam)
                        ? "F11" : _appConfig.HotkeyFullTeam;

                    // 避免重复绑定：若你可能多次进这段，可先 -= 再 +=
                    txtHotkeyChampSelect.KeyDown -= HotkeyTextBox_KeyDown;
                    txtHotkeyMyTeam.KeyDown -= HotkeyTextBox_KeyDown;
                    txtHotkeyFullTeam.KeyDown -= HotkeyTextBox_KeyDown;

                    txtHotkeyChampSelect.KeyDown += HotkeyTextBox_KeyDown;
                    txtHotkeyMyTeam.KeyDown += HotkeyTextBox_KeyDown;
                    txtHotkeyFullTeam.KeyDown += HotkeyTextBox_KeyDown;
                }

                // 把配置应用到钩子（必须在 _appConfig 已 Load、_hotkeyManager 已创建之后）
                _hotkeyManager?.ApplyConfig(_appConfig);

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
                chkUrf.CheckedChanged += ModeCheckBox_CheckedChanged;
                chkPickUrf.CheckedChanged += ModeCheckBox_CheckedChanged;
                chkClassic.CheckedChanged += ModeCheckBox_CheckedChanged;
                chkBot.CheckedChanged += ModeCheckBox_CheckedChanged;
                chkAutoAccept.CheckedChanged += AutoAccept_CheckedChanged;
                cboAutoAcceptDelay.SelectedIndexChanged += cboAutoAcceptDelay_SelectedIndexChanged;
                chkHideSelf.CheckedChanged += HideSelf_CheckedChanged;
                chkUseChampionName.CheckedChanged += UseChampionName_CheckedChanged;
                chkSkipHonor.CheckedChanged += ChkSkipHonor_CheckedChanged;
                chkSkipEndOfGameStats.CheckedChanged += ChkSkipEndOfGameStats_CheckedChanged;
                cboCardMatchCount.SelectedIndexChanged += cboCardMatchCount_SelectedIndexChanged;
                chkAutoSkinSelect.CheckedChanged += chkAutoSkinSelect_CheckedChanged;

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
        /// 启动 LCU 连接入口（兼容旧方法名「轮询」）。
        /// 实际已改为由 <see cref="LcuManager"/> 事件驱动：Connected / Disconnected / Reconnecting。
        /// </summary>
        public void StartLcuConnectPolling()
        {
            // 兼容旧调用名，实际已改为事件驱动
            _ = StartLcuConnectionAsync();
        }

        /// <summary>
        /// 订阅 LcuManager 事件并启动永久连接循环。
        /// 启动前会取消旧的 AsyncPoller，并防止事件重复订阅。
        /// 初始 UI 置为「未连接」，由后续 Connected 回调再恢复。
        /// </summary>
        private async Task StartLcuConnectionAsync()
        {
            try
            {
                // 先取消旧轮询（防止残留）
                _lcuPoller?.Stop();

                // 避免重复订阅：先解绑再绑定
                LcuManager.Instance.Connected -= OnLcuConnected;
                LcuManager.Instance.Disconnected -= OnLcuDisconnected;
                LcuManager.Instance.Reconnecting -= OnLcuReconnecting;

                LcuManager.Instance.Connected += OnLcuConnected;
                LcuManager.Instance.Disconnected += OnLcuDisconnected;
                LcuManager.Instance.Reconnecting += OnLcuReconnecting;

                // 启动时先显示「检测连接」状态
                _uiManager!.SetLcuUiState(connected: false, inGame: false);
                _statusBar?.SetDetecting();
                _statusBar?.ClearTip();

                Debug.WriteLine("[LCU] 启动 LcuManager 连接循环...");
                await LcuManager.Instance.StartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LCU] 启动连接失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 等待当前召唤师信息与 SGP 会话均就绪。
        /// 登录后 LCU 的 token/session 可能延迟数百毫秒到数秒，过早拉战绩会导致空白或失败。
        /// </summary>
        /// <param name="maxAttempts">最大重试次数，默认 20</param>
        /// <param name="delayMs">每次间隔毫秒，默认 800</param>
        /// <returns>summoner 与 SGP 均成功时返回 true，否则 false</returns>
        private async Task<bool> WaitForSessionAndSgpReadyAsync(int maxAttempts = 20, int delayMs = 800)
        {
            for (int i = 1; i <= maxAttempts; i++)
            {
                try
                {
                    var summoner = await Globals.lcuClient.GetCurrentSummoner();
                    bool summonerOk = summoner != null
                        && !string.IsNullOrEmpty(summoner["puuid"]?.ToString());

                    bool sgpOk = false;
                    if (summonerOk)
                    {
                        //sgpOk = await Globals.sgpClient.InitSgpAsync(Globals.lcuClient.Client);
                        sgpOk = await Globals.sgpClient.InitSgpAsync(Globals.lcuClient.Client,LcuManager.Instance.SgpServerId);
                    }

                    //Debug.WriteLine($"[就绪检查] 第 {i}/{maxAttempts} 次 → summoner={summonerOk}, sgp={sgpOk}");

                    if (summonerOk && sgpOk)
                        return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[就绪检查] 异常: {ex.Message}");
                }

                await Task.Delay(delayMs);
            }

            return false;
        }

        /// <summary>
        /// LcuManager 连接成功（含首次连接与重连成功）回调。
        /// 将 HttpClient 注入 <see cref="Globals.lcuClient"/>，再执行业务初始化；
        /// finally 中无论成败都按 LcuReady/IsGame 刷新一次 UI，避免卡在「检测中」。
        /// </summary>
        private async void OnLcuConnected()
        {
            try
            {
                Debug.WriteLine("[LCU] OnLcuConnected");

                // 把 LcuManager 建立好的 LcuClient 注入会话，供全项目 Globals.lcuClient 使用
                bool injected = Globals.lcuClient.AttachExistingClient(LcuManager.Instance.HttpClient);
                if (!injected)
                {
                    Debug.WriteLine("[LCU] 注入失败");
                    return;
                }

                _uiManager!.LcuReady = true;
                await InitializeAfterLcuConnected();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LCU] OnLcuConnected 异常: {ex}");
            }
            finally
            {
                // 无论中间是否超时/异常，最终按当前标志刷新一次 UI
                try
                {
                    _uiManager?.SetLcuUiState(_uiManager.LcuReady, _uiManager.IsGame);
                }
                catch { }
            }
        }

        /// <summary>
        /// LcuManager 确认真正断线时的回调（进程消失或连续重连失败等）。
        /// 清空连接相关 UI、停止游戏流程监视；不要在此再次 Start 连接，由 LcuManager 自行重试。
        /// </summary>
        private void OnLcuDisconnected()
        {
            Debug.WriteLine("[LCU] OnLcuDisconnected（确认断开）");

            _uiManager!.LcuReady = false;
            _uiManager!.IsGame = false;

            // 硬断开：显示「正在检测/重新连接…」并清空战绩相关面板
            _uiManager!.SetLcuUiState(false, false);
            // 状态栏：与面板一致，不要写「已断开」
            // LcuManager 主循环还在跑，下一轮还会重试
            _statusBar?.SetDetecting();
            _statusBar?.ClearTip();
            // 若希望文案更明确，可在 StatusBarManager 加：
            // _statusBar?.SetConnectionStatus("等待客户端...");

            _gameFlowWatcher?.ClearGameState();
            _gameFlowWatcher?.StopGameflowWatcher();
            // 不要在这里再调用 StartLcuConnectPolling()，LcuManager 内部已有自动重连循环
        }

        /// <summary>
        /// LcuManager 软重连回调：链路抖动或探活连续失败后的自动恢复阶段。
        /// 客户端进程往往仍在，故不要 SetLcuUiState(false) 清空战绩面板，避免误伤用户界面。
        /// 可选：在此更新状态栏/Toast 为「正在恢复连接…」。
        /// </summary>
        private void OnLcuReconnecting()
        {
            Debug.WriteLine("[LCU] OnLcuReconnecting");
            // 可选：状态栏/小提示「正在恢复连接…」，不要 SetLcuUiState(false) 清面板
            // 若你有 Toast/状态标签，可在这里改文字
            _statusBar?.SetReconnecting();
            _statusBar?.ClearTip();
        }

        /// <summary>
        /// LCU 连接成功后的业务初始化：资源、英雄、消息构建器、SGP、战绩 Tab、流程监视。
        /// 带会话/SGP 就绪等待，减轻首屏战绩空白；超时仍继续初始化，便于后续强制刷新。
        /// </summary>
        private async Task InitializeAfterLcuConnected()
        {
            // 加载静态资源（不依赖登录完全完成）
            Globals.resLoading.loadingResource(Globals.lcuClient);

            if (_championManager != null)
            {
                try
                {
                    Debug.WriteLine("[英雄预加载] 开始加载所有英雄数据...");
                    await _championManager.InitializeAsync(forceRefresh: false);
                    //Debug.WriteLine($"[英雄预加载] 成功！共加载 {_championManager.AllChampions.Count} 个英雄");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[英雄预加载] 失败: {ex.Message}");
                }
            }

            // 延迟创建 ChatMessageBuilder（依赖 ChampionManager）
            if (_chatMessageBuilder == null && _championManager != null)
            {
                _chatMessageBuilder = new ChatMessageBuilder(
                    _playerCardManager.GetAllCachedPlayerInfos,
                    _championManager!,
                    _appConfig?.HideSelfWhenSending ?? false
                );
                _chatSender?.SetChatMessageBuilder(_chatMessageBuilder);
                Debug.WriteLine("[ChatMessageBuilder] 已成功延迟创建并注入 ChampionManager");
            }

            // 等召唤师 + SGP 就绪后再拉战绩，减少空白首屏
            //Debug.WriteLine("[LCU] 等待会话与 SGP 就绪...");
            bool ready = await WaitForSessionAndSgpReadyAsync(maxAttempts: 20, delayMs: 800);
            if (!ready)
            {
                Debug.WriteLine("[LCU] 会话/SGP 等待超时，仍尝试初始化（可能空白，可后续强制刷新）");
                // 再试一次 SGP，不阻塞后续 phase 监听
                //_ = await Globals.sgpClient.InitSgpAsync(Globals.lcuClient.Client);
                _ = await Globals.sgpClient.InitSgpAsync(Globals.lcuClient.Client,LcuManager.Instance.SgpServerId);
            }
            else
            {
                Debug.WriteLine("SGP 连接初始化成功！");
            }

            //   状态栏，用中文大区名；没有 SgpServerId 时至少显示「已连接」
            //string region = SgpServerConfig.GetDisplayName(LcuManager.Instance.SgpServerId);
            //_statusBar?.SetConnected(
            //    string.IsNullOrWhiteSpace(LcuManager.Instance.SgpServerId) ? null : region);
            // SGP 初始化完成后（ready 判断之后）
            string sgpId = Globals.sgpClient.SgpServerId;           // SgpSession 解析到的
            if (string.IsNullOrWhiteSpace(sgpId))
                sgpId = LcuManager.Instance.SgpServerId;            // 命令行可能仍为空

            string regionName = SgpServerConfig.GetDisplayName(sgpId);
            _statusBar?.SetConnected(
                string.IsNullOrWhiteSpace(sgpId) ? null : regionName);

            Debug.WriteLine($"[状态栏] SgpId={sgpId}, 显示={regionName}");

            // 重建战绩列表 Tab 宿主
            InitializeMatchTabContent();

            // 默认 Tab、预热、当前 phase、启动流程监视（需在 UI 线程）
            await this.InvokeIfRequiredAsync(async () =>
            {
                RefreshState.ForceMatchRefresh = true; // 首次/重连后走网络，避免脏缓存
                await InitializeDefaultTab();
                _configUpdateManager!.PreWarmUiComponents();

                string? currentPhase = await Globals.lcuClient.GetGameflowPhase();
                if (!string.IsNullOrEmpty(currentPhase))
                    await _gameFlowWatcher!.OnGameflowPhaseChanged(currentPhase, null);

                _gameFlowWatcher!.StartGameflowWatcher();

                _uiManager!.SetLcuUiState(_uiManager.LcuReady, _uiManager.IsGame);

                if (_uiManager.IsGame && imageTabControl1.TabPages.Count > 1)
                    imageTabControl1.SelectedIndex = 1;

                // 若已经在游戏中，状态栏改成「游戏中」
                if (_uiManager.IsGame)
                    _statusBar?.SetInGame(regionName);
            });

            _uiManager!.SetLcuUiState(_uiManager!.LcuReady, _uiManager!.IsGame);
        }

        /// <summary>
        /// 在战绩列表面板中创建并挂载 <see cref="MatchTabContent"/>（先清空旧控件）。
        /// </summary>
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

        #region 战绩查询
        /// <summary>
        /// 默认查询当前客户端玩家对战数据
        /// </summary>
        public async Task InitializeDefaultTab()
        {
            //Debug.WriteLine($"[InitializeDefaultTab] 开始，ForceMatchRefresh={RefreshState.ForceMatchRefresh}");

            var summoner = await Globals.lcuClient.GetCurrentSummoner();
            string? puuid = summoner?["puuid"]?.ToString();
            if (summoner == null || string.IsNullOrEmpty(puuid))
            {
                Debug.WriteLine("[InitializeDefaultTab] 获取当前召唤师失败或无 puuid，跳过");
                return;
            }

            Globals.CurrentPuuid = puuid;
            Globals.CurrentSummonerName = summoner["gameName"]?.ToString();

            _chatMessageBuilder?.SetMyPuuid(Globals.CurrentPuuid);

            //Debug.WriteLine($"[InitializeDefaultTab] 当前玩家 PUUID: {Globals.CurrentPuuid}");

            var rankedStats = await GetRankedStatsAsync(puuid);
            string privacyStatus = GetPrivacyStatus(summoner);

            //Debug.WriteLine($"[InitializeDefaultTab] 调用 CreateNewTab，ForceMatchRefresh={RefreshState.ForceMatchRefresh}");

            _matchTabContent?.CreateNewTab(
                summoner["summonerId"]?.ToString() ?? "",
                summoner["gameName"]?.ToString() ?? "",
                summoner["tagLine"]?.ToString() ?? "",
                puuid,
                summoner["profileIconId"]?.ToString() ?? "",
                summoner["summonerLevel"]?.ToString() ?? "",
                privacyStatus,
                rankedStats
            );

            //Debug.WriteLine("[InitializeDefaultTab] 完成");
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

            // ★ 强制刷新时，先清除该玩家全部缓存，防止新旧分页数据错位
            if (refreshFlag)
            {
                MatchCache.ClearPlayer(puuid);
                //Debug.WriteLine($"[LoadFullMatchData] 强制刷新，已清除玩家缓存: {puuid}");
            }
            else
            {
                // 非强制刷新时优先读缓存
                var cached = MatchCache.Get(puuid, queueId, begIndex, pageSize);
                if (cached != null)
                    return cached;
            }

            // 发起网络请求
            var allGames = await Globals.sgpClient.SgpFetchLatestMatches(puuid, begIndex, pageSize, queueId);

            // 写入缓存,null 表示失败，不要写入缓存；空数组可以写入（表示“这页确实没数据”）
            MatchCache.Add(puuid, queueId, begIndex, pageSize, allGames);

            // 重置强制刷新状态（只生效一次）
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
        /// <summary>
        /// 获取当前发送模式（供 GameChatSender 调用）
        /// </summary>
        public bool GetSendMode()
        {
            return rbModeCustom?.Checked ?? false;
        }

        /// <summary>
        /// 获取自定义发送内容（供 GameChatSender 调用）
        /// </summary>
        public string GetCustomSendContent()
        {
            return txtCustomContent?.Text?.Trim() ?? "";
        }

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

        /// <summary>
        /// 当前焦点是否在热键配置框（改键时不触发发送、不吞键）
        /// </summary>
        public bool IsHotkeyCaptureFocused()
        {
            return (txtHotkeyChampSelect?.Focused ?? false)
                || (txtHotkeyMyTeam?.Focused ?? false)
                || (txtHotkeyFullTeam?.Focused ?? false);
        }

        private void HotkeyTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb || _appConfig == null) return;

            e.SuppressKeyPress = true;
            e.Handled = true;

            // Backspace / Delete → 该项恢复默认
            if (e.KeyCode is Keys.Back or Keys.Delete)
            {
                if (tb == txtHotkeyChampSelect)
                {
                    tb.Text = "Ctrl+F7";
                    _appConfig.HotkeyChampSelect = tb.Text;
                }
                else if (tb == txtHotkeyMyTeam)
                {
                    tb.Text = "F9";
                    _appConfig.HotkeyMyTeam = tb.Text;
                }
                else if (tb == txtHotkeyFullTeam)
                {
                    tb.Text = "F11";
                    _appConfig.HotkeyFullTeam = tb.Text;
                }

                SaveAppConfig();
                _hotkeyManager?.ApplyConfig(_appConfig);
                return;
            }

            // 忽略单独按下的修饰键
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
                return;

            string text = e.Control ? $"Ctrl+{e.KeyCode}" : e.KeyCode.ToString();

            // 三个动作不能绑同一个键
            foreach (var other in new[] { txtHotkeyChampSelect, txtHotkeyMyTeam, txtHotkeyFullTeam })
            {
                if (other != null && other != tb &&
                    string.Equals(other.Text, text, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("该快捷键已被其他动作使用，请换一个。", "快捷键冲突",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            tb.Text = text;

            if (tb == txtHotkeyChampSelect)
                _appConfig.HotkeyChampSelect = text;
            else if (tb == txtHotkeyMyTeam)
                _appConfig.HotkeyMyTeam = text;
            else if (tb == txtHotkeyFullTeam)
                _appConfig.HotkeyFullTeam = text;

            SaveAppConfig();
            _hotkeyManager?.ApplyConfig(_appConfig);
        }

        #endregion

        #region 自动化操作事件处理方法
        private void imageTabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            _uiManager!.HandleTabSelectionChanged(imageTabControl1.SelectedIndex, _tab1Poller);
        }

        /// <summary>
        /// 打开英雄预选配置面板
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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

        /// <summary>
        /// 浏览已有英雄 + 为每英雄选默认皮肤
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void labChampionSkinBrowser_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (!_uiManager!.LcuReady)
            {
                MessageBox.Show("请先登录英雄联盟客户端并等待连接成功！", "客户端未连接",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var form = new League.ChampionSkin.ChampionSkinBrowser(_appConfig!);
            form.Owner = this;
            form.ShowDialog();
        }

        /// <summary>
        /// 是否勾选卡片模式筛选
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void checkBoxFilterMode_CheckedChanged(object sender, EventArgs e)
        {
            // 更新内存中的配置
            _appConfig!.FilterByGameMode = checkBoxFilterMode.Checked;

            // 直接保存到文件
            LeagueConfigManager.Save(_appConfig);

            // 通知MatchQueryProcessor更新筛选模式
            _matchQueryProcessor?.SetFilterMode(checkBoxFilterMode.Checked);

            _matchQueryProcessor?.ClearPlayerMatchCache();

            Debug.WriteLine($"[配置] 过滤模式改为: {checkBoxFilterMode.Checked}，配置已保存");
        }

        /// <summary>
        /// 卡片模式查询数量配置
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void cboCardMatchCount_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_appConfig == null) return;

            _appConfig.CardMatchHistoryCount = cboCardMatchCount.SelectedIndex switch
            {
                0 => 10,    // 选中第1项 → 查10
                2 => 30,    // 选中第2项 → 查30
                3 => 50,    // 选中第3项 → 查50
                _ => 20     // 选中第4项 → 查20
            };

            _matchQueryProcessor?.SetMatchHistoryCount(_appConfig.CardMatchHistoryCount);
            SaveAppConfig();

            // 场数变了，旧缓存场数不一致，建议清掉选人战绩缓存
            _matchQueryProcessor?.ClearPlayerMatchCache();

            Debug.WriteLine($"[配置] 卡片战绩场数 = {_appConfig.CardMatchHistoryCount}");
        }

        /// <summary>
        /// 是否勾选模式中自动预选英雄
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ModeCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (_appConfig == null) return;

            _appConfig.EnablePreliminaryInNormal = chkNormal.Checked;     // 匹配
            _appConfig.EnablePreliminaryInRanked = chkRanked.Checked;     // 排位，单双/灵活
            _appConfig.EnablePreliminaryInAram = chkAram.Checked;         // 大乱斗
            _appConfig.EnablePreliminaryInNexusBlitz = chkNexus.Checked;  // 海克斯乱斗
            _appConfig.EnablePreliminaryInUrf = chkUrf.Checked;           // 无限乱斗
            _appConfig.EnablePreliminaryInPickUrf = chkPickUrf.Checked;   // 无限火力
            _appConfig.EnablePreliminaryInClassic = chkClassic.Checked;   // 经典模式
            _appConfig.EnablePreliminaryInBot = chkBot.Checked;           // 峡谷人机

            SaveAppConfig(); // 立即保存到 LeagueConfig.json

            Debug.WriteLine("[自动预选模式] 配置已更新并保存");
        }

        /// <summary>
        /// 自动接受对局
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AutoAccept_CheckedChanged(object sender, EventArgs e)
        {
            SaveAutoAcceptConfig();
        }

        /// <summary>
        /// 自动接受对局延时设置
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void cboAutoAcceptDelay_SelectedIndexChanged(object sender, EventArgs e)
        {
            SaveAutoAcceptConfig();
        }

        /// <summary>
        /// 保存自动接受对局与对局接受延时时间
        /// </summary>
        private void SaveAutoAcceptConfig()
        {
            if (_appConfig == null) return;

            _appConfig.EnableAutoAcceptQueue = chkAutoAccept.Checked;

            _appConfig.AutoAcceptDelaySeconds = cboAutoAcceptDelay.SelectedIndex switch
            {
                1 => 5,     // 选中第1项 → 存5
                2 => 10,    // 选中第2项 → 存10
                _ => 0      // 选中第0项 或 其他 → 存0
            };

            SaveAppConfig();
            Debug.WriteLine($"[自动接受] 开关={_appConfig.EnableAutoAcceptQueue}, 延迟={_appConfig.AutoAcceptDelaySeconds}秒");
        }

        /// <summary>
        /// 设置发送消息是战绩，还是自定义消息
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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

        /// <summary>
        /// 自定义消息配置自动保存
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TxtCustomContent_Leave(object sender, EventArgs e)
        {
            if (txtCustomContent == null || _appConfig == null) return;

            // 将当前文本框的内容同步到配置中
            _appConfig.CustomSendContent = txtCustomContent.Text;

            // 存盘
            SaveAppConfig();
        }

        /// <summary>
        /// 发送战绩时是否隐藏自己
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HideSelf_CheckedChanged(object sender, EventArgs e)
        {
            if (_appConfig == null) return;
            _appConfig.HideSelfWhenSending = chkHideSelf.Checked;
            SaveAppConfig();
            Debug.WriteLine($"[发送配置] 隐藏自己 已更新: {_appConfig.HideSelfWhenSending}");
        }

        /// <summary>
        /// 发送战绩时是否使用英雄名称
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseChampionName_CheckedChanged(object sender, EventArgs e)
        {
            if (_appConfig == null) return;
            _appConfig.UseChampionNameWhenSending = chkUseChampionName.Checked;
            SaveAppConfig();
            Debug.WriteLine($"[发送配置] 使用英雄名称 已更新: {_appConfig.UseChampionNameWhenSending}");
        }

        /// <summary>
        /// 游戏结束时是否跳过点赞界面
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChkSkipHonor_CheckedChanged(object? sender, EventArgs e)
        {
            if (_appConfig == null) return;
            _appConfig.EnableSkipHonor = chkSkipHonor.Checked;
            SaveAppConfig();
            Debug.WriteLine($"[游戏结束配置] 跳过点赞界面 已更新: {_appConfig.EnableSkipHonor}");
        }

        /// <summary>
        /// 游戏结束时是否跳过结算界面
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChkSkipEndOfGameStats_CheckedChanged(object? sender, EventArgs e)
        {
            if (_appConfig == null) return;
            _appConfig.EnableSkipEndOfGameStats = chkSkipEndOfGameStats.Checked;
            SaveAppConfig();
            Debug.WriteLine($"[游戏结束配置] 跳过结算界面 已更新: {_appConfig.EnableSkipEndOfGameStats}");
        }

        /// <summary>
        /// 是否自动配置英雄皮肤
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void chkAutoSkinSelect_CheckedChanged(object? sender, EventArgs e)
        {
            if (_appConfig == null) return;
            _appConfig.EnableAutoSkinSelect = chkAutoSkinSelect.Checked;
            SaveAppConfig();
            Debug.WriteLine($"[英雄皮肤自动配置] 自动配置皮肤 已更新: {_appConfig.EnableAutoSkinSelect}");
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

        #region 窗体UI事件方法

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_matchTabContent != null)
            {
                _matchTabContent.CleanupAllTabs(isFormClosing: true);
            }

            _gameFlowWatcher?.StopGameflowWatcher();
            _lcuPoller?.Stop();
            _tab1Poller?.Stop();

            // 状态栏
            _statusBar?.Dispose();
            _statusBar = null;

            // 停止 LcuManager
            LcuManager.Instance.Stop();

            // 取消事件订阅，防止内存泄漏
            LcuManager.Instance.Connected -= OnLcuConnected;
            LcuManager.Instance.Disconnected -= OnLcuDisconnected;
            LcuManager.Instance.Reconnecting -= OnLcuReconnecting;

            _hotkeyManager?.Dispose();
            base.OnFormClosing(e);
            _playerCardManager?.UiLock?.Dispose();
        }

        private void lk_GitPro_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            lk_GitPro.LinkVisited = true;

            // 直接使用 LinkLabel 的 Text 作为 URL
            string url = lk_GitPro.Text;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 添加第二个 LinkLabel 控件（假设命名为 lk_QQGroup）
        private void lk_QQGroup_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string groupNumber = lk_QQGroup.Text;   // 例如：123456789

            string url = $"http://qm.qq.com/cgi-bin/qm/qr?_wv=1027&k=D6PkzL9J67rjSKOFjH7FuLBNFlsE_GVe&authKey=tms7jm6kh844bBlq0fx4y3lUbHhwssJ75XbuvJK%2BBALqEKiua0%2FMG%2BXCyQSLDMvU&noverify=0&group_code={groupNumber}";

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,        // ← 关键！必须设为 true
                    Verb = "open"
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开QQ群失败：\n" + ex.Message +
                               "\n\n请确认已安装QQ客户端并登录！");
            }
        }

        /// <summary>
        /// 重启客户端界面（不退出登录）
        /// 适用于：选人卡住、大厅加载不全、按钮点不动等情况
        /// </summary>
        private async void btnRestartUx_Click(object sender, EventArgs e)
        {
            if (_clientUtility == null)
            {
                MessageBox.Show("工具未初始化", "提示");
                return;
            }

            btnRestartUx.Enabled = false;
            try
            {
                var (ok, msg) = await _clientUtility.RestartUxAsync();
                // 游戏内会直接返回说明文字；其它情况可按需弹窗
                MessageBox.Show(msg, "重启界面", MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            finally
            {
                btnRestartUx.Enabled = true;
            }
        }


        /// <summary>
        /// 强制结束所有相关客户端进程（解决残留进程问题）
        /// </summary>
        private void btnCloseClients_Click(object sender, EventArgs e)
        {
            if (_clientUtility == null)
                _clientUtility = new ClientUtilityService();

            if (MessageBox.Show(
                    "确定强制关闭所有客户端相关进程吗？\n" +
                    "会结束 LeagueClient / LeagueClientUx 等（不依赖是否已登录 LCU）。",
                    "确认关闭客户端",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            btnCloseClients.Enabled = false;
            try
            {
                var (anyKilled, msg) = _clientUtility.CloseAllClients();
                MessageBox.Show(msg, "关闭客户端", MessageBoxButtons.OK,
                    anyKilled ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            finally
            {
                btnCloseClients.Enabled = true;
            }
        }

        #endregion
    }
}