using League.Caching;
using League.Clients;
using League.Controls;
using League.Extensions;
using League.Infrastructure;
using League.Managers;
using League.Models;
using League.Networking;
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
        // 管理器实例
        private FormUiStateManager? _uiManager;
        private GameFlowWatcher? _gameFlowWatcher;
        private PlayerCardManager? _playerCardManager;
        private MatchQueryProcessor? _matchQueryProcessor;
        private MessageSender? _messageSender;
        private ConfigUpdateManager? _configUpdateManager;
        private MatchDetailManager? _matchDetailManager;

        // 原有字段
        private AsyncPoller _lcuPoller = new AsyncPoller();
        private MatchTabContent? _matchTabContent;
        private CancellationTokenSource? _watcherCts;
        private CancellationTokenSource? _champSelectCts;
        public int myTeamId = 0;
        public List<string> lastChampSelectSnapshot = new List<string>();
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
        private InGameChatSender? _inGameChatSender;

        public FormMain()
        {
            InitializeComponent();
            InitializeManagers();
        }

        private void InitializeManagers()
        {
            _uiManager = new FormUiStateManager(this);
            _matchQueryProcessor = new MatchQueryProcessor();
            _playerCardManager = new PlayerCardManager(this, _matchQueryProcessor);
            _gameFlowWatcher = new GameFlowWatcher(this, _uiManager, _playerCardManager, _matchQueryProcessor);
            _messageSender = new MessageSender();
            _configUpdateManager = new ConfigUpdateManager(this);
            _matchDetailManager = new MatchDetailManager(this);
            _chatMessageBuilder = new ChatMessageBuilder(GetCachedPlayerInfos);
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

                // 绑定事件（建议统一一个事件处理）
                chkNormal.CheckedChanged += ModeCheckBox_CheckedChanged;
                chkRanked.CheckedChanged += ModeCheckBox_CheckedChanged;
                chkAram.CheckedChanged += ModeCheckBox_CheckedChanged;
                chkNexus.CheckedChanged += ModeCheckBox_CheckedChanged;

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

                        await InitializeAfterLcuConnected();

                        // LCU 连接成功之后再创建消息发送监听
                        _inGameChatSender = new InGameChatSender(Globals.lcuClient);
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

        // 低级键盘钩子回调（核心）
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            if (wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                bool isCtrlDown = (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;

                // 只处理我们关心的按键
                bool isTargetKey = (key == Keys.F9 || key == Keys.F11 || key == Keys.F12 ||
                                   (isCtrlDown && key == Keys.F7));

                if (isTargetKey)
                {
                    DateTime now = DateTime.Now;
                    if ((now - _lastHookTrigger) < _debounceInterval)
                    {
                        Debug.WriteLine($"[钩子防抖] 忽略 {key}，间隔太短");
                        return (IntPtr)1; // 吞掉，避免重复
                    }
                    _lastHookTrigger = now;

                    string keyDesc = key switch
                    {
                        Keys.F9 => "F9 (我方)",
                        Keys.F11 => "F11 (我方-备用)",
                        Keys.F12 => "F12 (全队)",
                        Keys.F7 when isCtrlDown => "Ctrl+F7 (选人)",
                        _ => key.ToString()
                    };

                    Debug.WriteLine($"[钩子触发] {keyDesc} at {now:HH:mm:ss.fff} (Ctrl={isCtrlDown})");

                    // 异步处理，避免阻塞钩子回调
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            string phase = await GetGameflowPhaseSafe();

                            if (phase == "InProgress")
                            {
                                if (key == Keys.F9 || key == Keys.F11)
                                {
                                    await HandleMyTeam();
                                }
                                else if (key == Keys.F12)
                                {
                                    await HandleFullTeam();
                                }
                            }
                            else if (phase == "ChampSelect" && isCtrlDown && key == Keys.F7)
                            {
                                await HandleChampSelectF7();
                            }
                            else
                            {
                                Debug.WriteLine($"[钩子] 阶段不支持: {phase}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[钩子处理异常] {keyDesc} - {ex.Message}");
                        }
                    });

                    // 推荐：吞掉按键，让游戏收不到 F9/F12（避免红色框/截图）
                    return (IntPtr)1;
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
        #endregion

        #region 热键监听

        // 🔥 新增：选人阶段发送（Ctrl+F7）
        private async Task HandleChampSelectF7()
        {
            string phase = await GetGameflowPhaseSafe();
            if (phase != "ChampSelect")
            {
                Debug.WriteLine($"[Ctrl+F7] 当前不是选人阶段: {phase}");
                return;
            }

            var myTeam = _cachedMyTeam;
            if (myTeam == null || myTeam.Count == 0)
            {
                Debug.WriteLine("[Ctrl+F7] 我方队伍缓存为空");
                return;
            }

            string msg = _chatMessageBuilder!.BuildMyTeamSummary(myTeam);
            if (string.IsNullOrWhiteSpace(msg))
            {
                Debug.WriteLine("[Ctrl+F7] 生成的消息为空");
                return;
            }

            // 选人阶段发送（优先使用 LcuSession 里已有的方法）
            bool success = await Globals.lcuClient.SendChampSelectMessageAsync(msg);

            // 如果上面失败，再尝试用 InGameChatSender 的通用方式（兼容性更强）
            if (!success)
            {
                Debug.WriteLine("[Ctrl+F7] 选人发送失败，不再 fallback");
            }

            Debug.WriteLine($"[Ctrl+F7] 选人阶段发送 {(success ? "成功" : "失败")}");
        }

        // 🔥 新增：我方队伍发送（F9/F11通用）
        private async Task HandleMyTeam()
        {
            if (_inGameChatSender == null)
            {
                Debug.WriteLine("[HandleMyTeam] InGameChatSender 未初始化，跳过");
                return;
            }

            string phase = await GetGameflowPhaseSafe();
            if (phase != "InProgress")
            {
                Debug.WriteLine($"[F9] 非游戏阶段: {phase}");
                return;
            }

            var myTeam = _cachedMyTeam;
            if (myTeam == null || myTeam.Count == 0)
            {
                Debug.WriteLine("[F9] 我方队伍为空");
                return;
            }

            string msg = _chatMessageBuilder!.BuildMyTeamSummary(myTeam);
            if (string.IsNullOrWhiteSpace(msg)) return;

            //bool success = await Globals.lcuClient.SendInGameMessageAsync(msg);
            bool success = await Globals.lcuClient.SendMessageComprehensive(msg);
            //bool success = GameChatInputSender.Send(msg);

            Debug.WriteLine($"[F9] 发送结果: {(success ? "成功" : "失败")}");
        }

        // 🔥 新增：我方+敌方发送（F12）
        private async Task HandleFullTeam()
        {
            if (_inGameChatSender == null)
            {
                Debug.WriteLine("[HandleFullTeam] InGameChatSender 未初始化，跳过");
                return;
            }

            string phase = await GetGameflowPhaseSafe();
            if (phase != "InProgress")
            {
                Debug.WriteLine($"[F12] 非游戏阶段: {phase}");
                return;
            }

            var myTeam = _cachedMyTeam;
            var enemyTeam = _cachedEnemyTeam;
            if (myTeam?.Count > 0 == false || enemyTeam?.Count > 0 == false) return;

            string msg = _chatMessageBuilder!.BuildFullTeamSummary(myTeam, enemyTeam);
            if (string.IsNullOrWhiteSpace(msg)) return;

            bool success = await Globals.lcuClient.SendMessageComprehensive(msg);
            //bool success = GameChatInputSender.Send(msg);

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
            var summoner = await Globals.lcuClient.GetCurrentSummoner();
            if (summoner == null) return;

            Globals.CurrentPuuid = summoner["puuid"]?.ToString() ?? "";
            Globals.CurrentSummonerName = summoner["gameName"]?.ToString();

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

        // 为PlayerCardManager提供缓存访问
        public Dictionary<long, PlayerMatchInfo> GetCachedPlayerInfos()
        {
            // 直接返回 PlayerCardManager 的缓存（加 null 防护）
            if (_playerCardManager == null)
            {
                Debug.WriteLine("[警告] _playerCardManager 为 null，无法获取缓存");
                return new Dictionary<long, PlayerMatchInfo>();
            }

            lock (_playerCardManager._cachedPlayerMatchInfos)  // 注意加锁，防止并发问题
            {
                return new Dictionary<long, PlayerMatchInfo>(_playerCardManager._cachedPlayerMatchInfos);
            }
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_matchTabContent != null)
            {
                _matchTabContent.CleanupAllTabs();
            }

            _gameFlowWatcher?.StopGameflowWatcher();
            _lcuPoller?.Stop();
            _tab1Poller?.Stop();

            // 卸载热键钩子
            UninstallKeyboardHook();

            base.OnFormClosing(e);
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