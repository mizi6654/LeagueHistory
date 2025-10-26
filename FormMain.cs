using League.model;
using League.uitls;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace League
{
    public partial class FormMain : Form
    {
        //用来轮询检测是否已经连接lcu api客户端
        private AsyncPoller _lcuPoller = new AsyncPoller();
        private bool lcuReady = false; // 表示是否已经初始化过了

        private MatchTabContent _matchTabContent;

        private CancellationTokenSource _watcherCts;

        //OnChampSelectStart() 里启动一个 内部轮询任务
        private CancellationTokenSource _champSelectCts;

        private int myTeamId = 0;
        //存储我方队伍选择的英雄状态
        private List<string> lastChampSelectSnapshot = new List<string>();

        //判断是否已有缓存，如果有直接返回缓存，跳过网络查询
        private readonly Dictionary<long, PlayerMatchInfo> _cachedPlayerMatchInfos = new Dictionary<long, PlayerMatchInfo>();

        //显示卡片并缓存当前 summoner → championId
        private Dictionary<long, int> _currentChampBySummoner = new Dictionary<long, int>();
        private Dictionary<long, int> _summonerToColMap = new Dictionary<long, int>(); // optional: 提供一层对位映射

        // 玩家头像全局缓存
        private static readonly ConcurrentDictionary<string, Image> _imageCache = new();

        // key: summonerId, value: 缓存的对局信息
        private Dictionary<long, PlayerMatchInfo> playerMatchCache = new Dictionary<long, PlayerMatchInfo>();
        private Dictionary<(int row, int column), (long summonerId, int championId)> playerCache = new Dictionary<(int, int), (long, int)>();

        // summonerId → PlayerCardControl 映射，便于染色时快速获取控件
        private readonly ConcurrentDictionary<long, PlayerCardControl> _cardBySummonerId = new();

        private bool _gameEndHandled = false;

        // 类成员声明，确保只创建一次，用来显示侧边栏tabControl提示文本
        private ToolTip tip = new ToolTip();
        private int _lastIndex = -1;

        private readonly Poller _tab1Poller = new Poller();
        private Panel _waitingPanel;

        bool _isGame = false;

        private bool _champSelectMessageSent = false;   //队伍选人阶段发送消息标志

        //定义两个字段，用来存储我方队伍与敌方队伍的数据
        private JArray _cachedMyTeam;
        private JArray _cachedEnemyTeam;

        // 显示与隐藏加载提示面板
        private Panel _loadingPanel;
        private Label _loadingLabel;
        private Panel _loadingOverlay;

        // 在类级别添加字段
        private DetailedViewLayoutManager _detailedViewLayoutManager;
        private TableViewManager _tableViewManager;

        private ToolTip _toolTip = new ToolTip();

        public FormMain()
        {
            InitializeComponent();
        }

        public static class Globals
        {
            public static LcuSession lcuClient = new LcuSession();
            public static SgpSession sgpClient = new SgpSession();
            public static ResourceLoading resLoading = new ResourceLoading();
            public static string CurrentSummonerId;
            public static string CurrentPuuid;
            public static string CurrGameMod;
        }

        #region 点击按钮弹窗提示
        /// <summary>
        /// 显示轻柔半透明加载提示（局部遮罩 + 居中）
        /// </summary>
        private void ShowLoadingIndicator()
        {
            SafeInvoke(this, () =>
            {
                // 若已有，直接返回
                if (_loadingOverlay != null && !_loadingOverlay.IsDisposed)
                    return;

                // 灰色半透明遮罩，仅覆盖中间区域（不全屏）
                _loadingOverlay = new Panel
                {
                    Size = new Size((int)(Width * 0.6), (int)(Height * 0.4)),
                    BackColor = Color.FromArgb(160, 255, 255, 255), // 白色半透明
                    BorderStyle = BorderStyle.FixedSingle,
                    Visible = false // 初始隐藏，用于渐显
                };

                // 居中定位
                _loadingOverlay.Location = new Point(
                    (ClientSize.Width - _loadingOverlay.Width) / 2,
                    (ClientSize.Height - _loadingOverlay.Height) / 2
                );

                // 提示文本
                var lbl = new Label
                {
                    Text = "正在加载，请稍候...",
                    Font = new Font("微软雅黑", 12, FontStyle.Regular),
                    ForeColor = Color.DimGray,
                    AutoSize = true,
                    BackColor = Color.Transparent
                };

                lbl.Location = new Point(
                    (_loadingOverlay.Width - lbl.Width) / 2,
                    (_loadingOverlay.Height - lbl.Height) / 2
                );
                _loadingOverlay.Controls.Add(lbl);

                // 添加到窗体顶层
                Controls.Add(_loadingOverlay);
                _loadingOverlay.BringToFront();

                // 渐显动画
                _loadingOverlay.Visible = true;
                _loadingOverlay.BackColor = Color.FromArgb(0, 255, 255, 255);

                var timer = new System.Windows.Forms.Timer { Interval = 20 };
                int alpha = 0;
                timer.Tick += (s, e) =>
                {
                    alpha += 15;
                    if (alpha >= 160)
                    {
                        alpha = 160;
                        timer.Stop();
                        timer.Dispose();
                    }
                    _loadingOverlay.BackColor = Color.FromArgb(alpha, 255, 255, 255);
                };
                timer.Start();
            });
        }

        /// <summary>
        /// 隐藏加载提示（渐隐）
        /// </summary>
        private void HideLoadingIndicator()
        {
            SafeInvoke(this, () =>
            {
                if (_loadingOverlay == null) return;

                var timer = new System.Windows.Forms.Timer { Interval = 20 };
                int alpha = 160;
                timer.Tick += (s, e) =>
                {
                    alpha -= 15;
                    if (alpha <= 0)
                    {
                        timer.Stop();
                        timer.Dispose();

                        Controls.Remove(_loadingOverlay);
                        _loadingOverlay.Dispose();
                        _loadingOverlay = null;
                    }
                    else
                    {
                        _loadingOverlay.BackColor = Color.FromArgb(alpha, 255, 255, 255);
                    }
                };
                timer.Start();
            });
        }
        #endregion

        #region 程序更新检测
        // 在主程序启动时调用的代码
        private async Task CheckForUpdates()
        {
            try
            {
                // 读取本地版本
                var localVersion = VersionInfo.GetLocalVersion();

                // 获取远程版本  
                var remoteVersion = await VersionInfo.GetRemoteVersion();

                if (remoteVersion != null && localVersion != null)
                {
                    // 直接比较字符串版本号
                    if (remoteVersion.version != localVersion.version)
                    {
                        var changelogStr = string.Join("\n", remoteVersion.changelog);
                        var msg = $"检测到新版本 {remoteVersion.version} ({remoteVersion.date})\n\n更新内容：\n{changelogStr}\n\n点击确定将打开下载页面，请手动下载解压使用。";

                        var result = MessageBox.Show(msg, "版本更新", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                        if (result == DialogResult.OK)
                        {
                            try
                            {
                                // 先写入新版本号，避免重复提示
                                string versionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
                                File.WriteAllText(versionFilePath, remoteVersion.version);
                                Debug.WriteLine($"已更新本地版本号: {remoteVersion.version}");

                                // 然后打开网盘链接
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = remoteVersion.updateUrl,
                                    UseShellExecute = true
                                });

                                Debug.WriteLine($"已打开下载链接: {remoteVersion.updateUrl}");
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"打开下载链接失败: {ex.Message}\n请手动输入链接访问: {remoteVersion.updateUrl}",
                                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        else
                        {
                            // 用户点击取消，可以选择记录"忽略此版本"
                            Debug.WriteLine("用户取消更新");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[更新检查] 异常: {ex}");
                MessageBox.Show($"检查更新时发生错误: {ex.Message}",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        #endregion
        /// <summary>
        /// 窗体加载事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FormMain_Load(object sender, EventArgs e)
        {
            try
            {
                var img1 = Image.FromFile(AppDomain.CurrentDomain.BaseDirectory + @"\Assets\Defaults\01.png");
                var img2 = Image.FromFile(AppDomain.CurrentDomain.BaseDirectory + @"\Assets\Defaults\02.png");
                var img3 = Image.FromFile(AppDomain.CurrentDomain.BaseDirectory + @"\Assets\Defaults\03.png");

                imageTabControl1.TabPages[0].Tag = img1;
                imageTabControl1.TabPages[1].Tag = img2;
                imageTabControl1.TabPages[2].Tag = img3;

                tip = new ToolTip
                {
                    AutoPopDelay = 5000,  // 提示显示 5 秒
                    InitialDelay = 100,   // 鼠标悬停 0.5 秒后才显示
                    ReshowDelay = 100,    // 再次显示的延迟
                    ShowAlways = true    // 即使控件不在活动状态也显示
                };

                // 绑定 MouseMove 事件，动态显示对应标签的提示
                imageTabControl1.MouseMove += ImageTabControl1_MouseMove;

                // 检查更新（不等待，避免阻塞UI）
                _ = CheckForUpdates();

                // 启动轮询 LCU 检测
                StartLcuConnectPolling();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[全局初始化异常] {ex.Message}");
            }
        }

        private void ImageTabControl1_MouseMove(object sender, MouseEventArgs e)
        {
            for (int i = 0; i < imageTabControl1.TabPages.Count; i++)
            {
                Rectangle r = imageTabControl1.GetTabRect(i);
                if (r.Contains(e.Location))
                {
                    if (_lastIndex != i)
                    {
                        _lastIndex = i;

                        // 将屏幕坐标转成控件坐标
                        Point clientPos = imageTabControl1.PointToClient(Cursor.Position);
                        // 在鼠标附近显示 ToolTip
                        tip.Show(imageTabControl1.TabPages[i].Text, imageTabControl1, clientPos.X + 10, clientPos.Y + 10, 1500);
                    }
                    return;
                }
            }
            // 鼠标不在任何标签上，清除提示
            tip.SetToolTip(imageTabControl1, null);
            _lastIndex = -1;
        }

        /// <summary>
        /// 查询按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void btn_search_Click(object sender, EventArgs e)
        {
            btn_search.Enabled = false;
            btn_search.Text = "查询中...";
            ShowLoadingIndicator();
            try
            {
                if (!lcuReady)
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
                JObject summoner = await Globals.lcuClient.GetSummonerByNameAsync(input);
                if (summoner == null)
                {
                    MessageBox.Show("玩家不存在,本软件只能查询相同大区玩家!");
                    return;
                }
                RefreshState.ForceMatchRefresh = true;
                Dictionary<string, RankedStats> rankedStats = RankedStats.FromJson(await Globals.lcuClient.GetCurrentRankedStatsAsync(summoner["puuid"].ToString()));
                string privacyStatus = "隐藏";
                if (summoner["privacy"]?.ToString().Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    privacyStatus = "公开";
                }
                _matchTabContent.CreateNewTab(summoner["gameName"].ToString(), summoner["tagLine"].ToString(), summoner["puuid"].ToString(), summoner["profileIconId"].ToString(), summoner["summonerLevel"].ToString(), privacyStatus, rankedStats);
            }
            catch (Exception ex)
            {
                Exception ex2 = ex;
                MessageBox.Show("查询失败: " + ex2.Message);
            }
            finally
            {
                HideLoadingIndicator();
                btn_search.Enabled = true;
                btn_search.Text = "查询";
            }
        }


        #region 软件启动，轮询检测 LCU 连接
        /// <summary>
        /// 启动窗口，轮询监听是否登录了lcu客户端
        /// </summary>
        private void StartLcuConnectPolling()
        {
            SetLcuUiState(connected: false, inGame: false);
            _lcuPoller.Start(async delegate
            {
                if (!lcuReady)
                {
                    if (await Globals.lcuClient.InitializeAsync())
                    {
                        lcuReady = true;
                        _lcuPoller.Stop();
                        Globals.resLoading.loadingResource(Globals.lcuClient);
                        if (await Globals.sgpClient.InitSgpAsync(Globals.lcuClient.Client))
                        {
                            Debug.WriteLine("SGP 连接初始化成功！");
                        }
                        else
                        {
                            Debug.WriteLine("SGP 连接初始化失败！");
                        }
                        SafeInvoke(panelMatchList, delegate
                        {
                            panelMatchList.Controls.Clear();
                            _matchTabContent = new MatchTabContent();
                            _matchTabContent.Dock = DockStyle.Fill;
                            panelMatchList.Controls.Add(_matchTabContent);
                        });
                        this.InvokeIfRequired(async delegate
                        {
                            RefreshState.ForceMatchRefresh = false;
                            await InitializeDefaultTab();
                            PreWarmUiComponents();
                            string currentPhase = await Globals.lcuClient.GetGameflowPhase();
                            if (!string.IsNullOrEmpty(currentPhase))
                            {
                                Debug.WriteLine("[LCU检测] 当前 phase = " + currentPhase);
                                await HandleGameflowPhase(currentPhase, null);
                            }
                            StartGameflowWatcher();
                        });
                        SetLcuUiState(lcuReady, _isGame);
                    }
                    else
                    {
                        Debug.WriteLine("[LCU检测中] 未找到 LCU 客户端");
                    }
                }
            }, 5000);
        }
        #endregion

        #region 轮询检测lcu 连接自动查询当前玩家战绩
        /// <summary>
        /// 默认查询当前客户端玩家对战数据
        /// </summary>
        /// <returns></returns>
        private async Task InitializeDefaultTab()
        {
            var summoner = await Globals.lcuClient.GetCurrentSummoner();
            if (summoner == null) return;

            Globals.CurrentPuuid = summoner["puuid"].ToString();

            var rankedJson = await Globals.lcuClient.GetCurrentRankedStatsAsync(summoner["puuid"].ToString());
            var rankedStats = RankedStats.FromJson(rankedJson);

            string privacyStatus = "隐藏";
            if (summoner["privacy"]?.ToString().Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) == true)
                privacyStatus = "公开";

            _matchTabContent.CreateNewTab(
                summoner["gameName"].ToString(),
                summoner["tagLine"].ToString(),
                summoner["puuid"].ToString(),
                summoner["profileIconId"].ToString(),
                summoner["summonerLevel"].ToString(),
                privacyStatus,
                rankedStats
            );
        }
        #endregion

        #region 预热玩家信息面板控件，避免卡顿
        /// <summary>
        /// 预热 UI 控件与异步方法，避免首次交互卡顿
        /// 必须在 LCU/资源初始化之后调用（比如 InitializeDefaultTab 执行完后）
        /// <summary>
        /// 预热 UI 控件与异步方法，避免首次交互卡顿
        /// </summary>
        private void PreWarmUiComponents()
        {
            // 1️⃣ 在 UI 线程上快速创建并释放一次 MatchTabPageContent
            // 用现有的 SafeInvoke，传入 this 表示主窗体控件
            SafeInvoke(this, () =>
            {
                try
                {
                    using (var dummy = new MatchTabPageContent())
                    {
                        // 强制创建句柄（包括子控件）
                        dummy.CreateControl();

                        // 强制一次 GDI 创建，触发字体/渲染初始化
                        using (var g = dummy.CreateGraphics())
                        {
                            // 空操作即可
                        }
                    }

                    Debug.WriteLine("UI 预热完成（控件实例化）");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"UI 预热失败: {ex}");
                }
            });

            // 2️⃣ 后台预热异步方法 / HTTP / 资源加载（不会阻塞UI）
            Task.Run(async () =>
            {
                try
                {
                    // 提前让 LCU 客户端跑一次请求，触发 HttpClient、SSL、JIT 等初始化
                    try { await Globals.lcuClient.GetSummonerByNameAsync("fake_prewarm#0000"); } catch { }
                    try { await Globals.lcuClient.GetCurrentRankedStatsAsync("fake-prewarm-puuid"); } catch { }

                    // 资源加载器预热（如果支持异步加载，调用轻量资源）
                    try
                    {
                        await Globals.resLoading.GetChampionInfoAsync(1);
                        await Profileicon.GetProfileIconAsync(1);
                    }
                    catch { }

                    Debug.WriteLine("后台预热完成（异步方法 + 资源）");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"后台预热失败: {ex}");
                }
            });
        }
        #endregion

        #region LCU 连接成功，开始监听玩家状态
        /// <summary>
        /// 监听玩家进入游戏房间状态，并实时获取玩家信息
        /// </summary>
        private async void StartGameflowWatcher()
        {
            if (!lcuReady)
            {
                return;
            }

            try
            {
                _watcherCts = new CancellationTokenSource();
                var token = _watcherCts.Token;
                string lastPhase = null;
                string previousPhase = null;

                await Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        string phase = await Globals.lcuClient.GetGameflowPhase();

                        if (string.IsNullOrEmpty(phase))
                        {
                            //返回空，则视为掉线
                            OnLcuDisconnected();
                            break;
                        }

                        if (phase != lastPhase)
                        {
                            Debug.WriteLine($"[Gameflow] 状态改变: {lastPhase} → {phase}");

                            //如果不返回空，则进入状态判断
                            await HandleGameflowPhase(phase, lastPhase);
                            previousPhase = lastPhase;
                            lastPhase = phase;
                        }

                        await Task.Delay(1000);
                    }

                }, token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"监听异常：{ex}");
            }
        }
        #endregion

        #region 根据监听不同的状态执行任务
        //封装 phase 处理
        private async Task HandleGameflowPhase(string phase, string previousPhase)
        {
            switch (phase)
            {
                case "Matchmaking":
                case "ReadyCheck":
                    _isGame = false;
                    ClearGameState();
                    SafeInvoke(imageTabControl1, () =>
                    {
                        SetLcuUiState(lcuReady, _isGame);
                        imageTabControl1.SelectedIndex = 1;
                    });

                    //离开选人时清掉发送消息标志
                    _champSelectMessageSent = false;
                    break;

                case "ChampSelect":
                    _isGame = true;

                    SafeInvoke(penalGameMatchData, () =>
                    {
                        //进入游戏选人房间，先清空前面的UI提示控件
                        if (_waitingPanel != null && penalGameMatchData.Controls.Contains(_waitingPanel))
                        {
                            penalGameMatchData.Controls.Remove(_waitingPanel);
                            _waitingPanel.Dispose();
                            _waitingPanel = null;
                        }

                        //判断是否存在tableLayoutPanel1，不存在则添加，它是用来显示玩家战绩的控件
                        if (!penalGameMatchData.Controls.Contains(tableLayoutPanel1))
                        {
                            tableLayoutPanel1.Dock = DockStyle.Fill;
                            penalGameMatchData.Controls.Add(tableLayoutPanel1);
                        }

                        tableLayoutPanel1.Visible = true;
                        tableLayoutPanel1.Controls.Clear();
                    });

                    _gameEndHandled = false;

                    //开始获取我方队伍玩家数据
                    await OnChampSelectStart();
                    break;

                case "InProgress":
                    //停止我方英雄获取轮询
                    _champSelectCts?.Cancel();

                    //离开选人时清掉发送消息标志
                    _champSelectMessageSent = false;

                    //开始获取敌方队伍玩家数据
                    await ShowEnemyTeamCards();
                    break;

                case "EndOfGame":
                case "PreEndOfGame":
                case "WaitingForStats":
                case "Lobby":
                case "None":
                    //离开选人时清掉发送消息标志
                    _champSelectMessageSent = false;

                    if (!_gameEndHandled &&
                        (previousPhase == "InProgress" || previousPhase == "WaitingForStats" || previousPhase == "ChampSelect"))
                    {
                        _gameEndHandled = true;
                        await OnGameEnd();
                    }
                    break;
            }
        }
        #endregion

        /// <summary>
        /// 监听房间状态为：ChampSelect，显示我方英雄列表卡片
        /// </summary>
        /// <returns></returns>
        private async Task OnChampSelectStart()
        {
            _champSelectCts?.Cancel(); // 若之前已有轮询，先取消
            _champSelectCts = new CancellationTokenSource();
            var token = _champSelectCts.Token;

            Debug.WriteLine("进入选人阶段");

            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var phase = await Globals.lcuClient.GetGameflowPhase();
                        if (phase != "ChampSelect") break;

                        await ShowMyTeamCards(); // 刷新选人信息

                        await Task.Delay(2000, token); // 每2秒刷新一次
                    }
                    catch (TaskCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("选人阶段轮询异常：" + ex.Message);
                    }
                }
            }, token);
        }

        //封装游戏状态清理
        private void ClearGameState()
        {
            lastChampSelectSnapshot.Clear();
            _currentChampBySummoner.Clear();
            _summonerToColMap.Clear();
            _cachedPlayerMatchInfos.Clear();
            playerMatchCache.Clear();
            _cardBySummonerId.Clear();
        }

        //封装断线处理
        private void OnLcuDisconnected()
        {
            lcuReady = false;
            _isGame = false;

            _watcherCts?.Cancel();
            SetLcuUiState(false, false);
            StartLcuConnectPolling();
        }

        //游戏结束
        private async Task OnGameEnd()
        {
            Debug.WriteLine("游戏已结束，正在清空缓存及队伍存储信息，重置UI");
            playerMatchCache.Clear();
            playerCache.Clear();
            _champSelectCts?.Cancel();
            lastChampSelectSnapshot.Clear();
            RefreshState.ForceMatchRefresh = true;
            this.InvokeIfRequired(async delegate
            {
                Debug.WriteLine("即将重置主 Tab 页内容...");
                await InitializeDefaultTab();
            });
        }

        private void StopGameflowWatcher()
        {
            _watcherCts?.Cancel();
        }
        

        #region 创建英雄卡片
        private async Task CreateBasicCardsOnly(JArray team, bool isMyTeam, int row)
        {
            Debug.WriteLine($"[CreateBasicCardsOnly] 开始创建 {(isMyTeam ? "我方" : "敌方")} 卡片，行号: {row}");
            int col = 0;

            foreach (var p in team)
            {
                long summonerId = (long)p["summonerId"];
                int championId = (int)p["championId"];

                // 英雄没变则跳过加载
                if (_currentChampBySummoner.TryGetValue(summonerId, out int prevChampId) && prevChampId == championId)
                {
                    _summonerToColMap[summonerId] = col++;
                    //Debug.WriteLine($"[CreateBasicCardsOnly] summonerId={summonerId} 英雄未变，跳过头像加载，col={col - 1}");
                    continue;
                }

                // 更新快照字典
                _currentChampBySummoner[summonerId] = championId;
                _summonerToColMap[summonerId] = col;

                string championName = Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown";
                Image avatar = await Globals.resLoading.GetChampionIconAsync(championId);

                var player = new PlayerInfo
                {
                    SummonerId = summonerId,
                    ChampionId = championId,
                    ChampionName = championName,
                    Avatar = avatar,
                    GameName = "加载中...",
                    IsPublic = "[加载中]",
                    SoloRank = "加载中...",
                    FlexRank = "加载中..."
                };

                var matchInfo = new PlayerMatchInfo
                {
                    Player = player,
                    MatchItems = new List<ListViewItem>(),
                    HeroIcons = new ImageList()
                };

                //Debug.WriteLine($"[CreateBasicCardsOnly] 创建卡片 summonerId={summonerId}, championId={championId}, col={col}");
                UpdateOrCreateLoadingPlayerMatch(matchInfo, isMyTeam, row, col);

                col++;
            }

            Debug.WriteLine($"[CreateBasicCardsOnly] 完成 {(isMyTeam ? "我方" : "敌方")} 卡片创建，共 {col} 个玩家");
        }
        #endregion

        #region 开始创建玩家卡片，优先创建头像
        private async Task FillPlayerMatchInfoAsync(JArray team, bool isMyTeam, int row)
        {
            Debug.WriteLine($"[FillPlayerMatchInfoAsync] 开始异步战绩查询 {(isMyTeam ? "我方" : "敌方")}，行号: {row}");

            var fetchedInfos = await RunWithLimitedConcurrency(
                team,
                async p =>
                {
                    long sid = p["summonerId"]?.Value<long>() ?? 0;
                    int cid = p["championId"]?.Value<int>() ?? 0;

                    PlayerMatchInfo info;

                    // 先看缓存是否有
                    lock (_cachedPlayerMatchInfos)
                    {
                        if (_cachedPlayerMatchInfos.TryGetValue(sid, out info))
                        {
                            //Debug.WriteLine($"[使用缓存] summonerId={sid}");

                            if (_currentChampBySummoner.TryGetValue(sid, out int current) && current == cid)
                            {
                                int col = _summonerToColMap.TryGetValue(sid, out int c) ? c : 0;

                                // 判断卡片是否仍为“加载中”
                                var panel = tableLayoutPanel1.GetControlFromPosition(col, row) as BorderPanel;
                                var card = panel?.Controls.Count > 0 ? panel.Controls[0] as PlayerCardControl : null;

                                if (card != null && card.IsLoading)
                                {
                                    Debug.WriteLine($"[刷新加载中卡片] summonerId={sid}");
                                    CreateLoadingPlayerMatch(info, isMyTeam, row, col);
                                }
                            }

                            return info;
                        }
                    }

                    // 非缓存命中，执行请求
                    Debug.WriteLine($"[战绩任务] 查询开始 summonerId={sid}, championId={cid}");
                    info = await SafeFetchPlayerMatchInfoAsync(p);
                    if (info == null)
                    {
                        Debug.WriteLine($"[跳过] summonerId={sid} 获取失败，info 为 null");

                        // 构造一个失败卡片并显示
                        var failedInfo = new PlayerMatchInfo
                        {
                            Player = new PlayerInfo
                            {
                                SummonerId = sid,
                                ChampionId = cid,
                                ChampionName = "查询失败",
                                GameName = "失败",
                                IsPublic = "[失败]",
                                SoloRank = "失败",
                                FlexRank = "失败",
                                Avatar = LoadErrorImage() // 替换为你自己的错误图
                            },
                            MatchItems = new List<ListViewItem>(),
                            HeroIcons = new ImageList()
                        };

                        int col = _summonerToColMap.TryGetValue(sid, out int c2) ? c2 : 0;
                        CreateLoadingPlayerMatch(failedInfo, isMyTeam, row, col);

                        return null;
                    }

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

            Debug.WriteLine($"[FillPlayerMatchInfoAsync] 异步战绩查询完成，共获取 {fetchedInfos.Count} 条");

            // 分析组队关系，仅对非 null 的 info 生效
            var detector = new PartyDetector();
            detector.Detect(fetchedInfos.Where(f => f != null).ToList());

            // 更新颜色
            foreach (var info in fetchedInfos)
            {
                if (info?.Player == null) continue;
                UpdatePlayerNameColor(info.Player.SummonerId, info.Player.NameColor);
            }
        }
        #endregion

        #region 显示我方队伍卡片
        private async Task ShowMyTeamCards()
        {
            //Debug.WriteLine("[ShowMyTeamCards] 获取选人会话中...");
            var session = await Globals.lcuClient.GetChampSelectSession();
            if (session == null)
            {
                Debug.WriteLine("[ShowMyTeamCards] 获取失败: session == null");
                return;
            }

            Globals.CurrGameMod = session["queueId"]?.ToString();
            Debug.WriteLine($"当前游戏模式：{Globals.CurrGameMod}");

            var myTeam = session["myTeam"] as JArray;
            if (myTeam == null || myTeam.Count == 0)
            {
                Debug.WriteLine("[ShowMyTeamCards] 获取失败: myTeam 数据为空");
                return;
            }

            myTeamId = (int)myTeam[0]["team"];
            int row = myTeamId - 1;
            //Debug.WriteLine($"[ShowMyTeamCards] 我的队伍 teamId={myTeamId}, row={row}");

            // 生成当前快照
            var currentSnapshot = new List<string>();
            foreach (var player in myTeam)
            {
                long summonerId = (long)player["summonerId"];
                int championId = (int)player["championId"];
                currentSnapshot.Add($"{summonerId}:{championId}");
            }

            // 比较快照，若一致则不更新
            if (lastChampSelectSnapshot.SequenceEqual(currentSnapshot))
            {
                //Debug.WriteLine("[ShowMyTeamCards] 队伍未变化，跳过刷新");
                return;
            }

            // 保存快照
            lastChampSelectSnapshot = currentSnapshot;

            //Debug.WriteLine("[ShowMyTeamCards] 队伍变化，开始刷新");

            //将我方数据存储
            _cachedMyTeam = myTeam;

            // 刷新 UI 和战绩
            await CreateBasicCardsOnly(myTeam, isMyTeam: true, row: row);
            //_ = FillPlayerMatchInfoAsync(myTeam, isMyTeam: true, row: row);
            await FillPlayerMatchInfoAsync(myTeam, isMyTeam: true, row: row);

            // 启动热键监听
            if (!_champSelectMessageSent)
            {
                ListenAndSendMessageWhenHotkeyPressed(myTeam);
                _champSelectMessageSent = true;
            }
        }
        #endregion

        #region 显示我方卡片时，创建选人窗口发送近期战绩方法
        // 声明 Win32 API
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(Keys vKey);

        private void ListenAndSendMessageWhenHotkeyPressed(JArray myTeam)
        {
            Task.Run(() =>
            {
                Debug.WriteLine("[HotKey] 等待用户按下快捷键 F9...");

                while (true)
                {
                    if ((GetAsyncKeyState(Keys.F9) & 0x8000) != 0)
                    {
                        Debug.WriteLine("[HotKey] 检测到 F9 被按下！");

                        // 切回 UI 线程（保证 STA）
                        Invoke((MethodInvoker)(() =>
                        {
                            SendChampSelectSummaryViaSendKeys(myTeam);
                        }));

                        break;
                    }

                    Thread.Sleep(100);
                }
            });
        }
        #endregion

        #region 选人窗口要发送的我方队伍战绩信息
        /// <summary>
        /// 发送我方队伍数据到选人聊天窗口
        /// </summary>
        private void SendChampSelectSummaryViaSendKeys(JArray myTeam)
        {
            var sb = new StringBuilder();

            foreach (var p in myTeam)
            {
                long summonerId = (long)p["summonerId"];
                if (!_cachedPlayerMatchInfos.TryGetValue(summonerId, out var info))
                {
                    continue;
                }

                //获取当前的puuid
                string puuid = (string)p["puuid"];
                //判断是否与窗口加载时的puuid一样，一样则是自己的，路过不发送
                if (!string.IsNullOrEmpty(puuid) && string.Equals(puuid, Globals.CurrentPuuid, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[跳过发送] 当前玩家:{p["gameName"].ToString()}");
                    continue;
                }

                string name = info.Player.GameName ?? "未知";
                string solo = info.Player.SoloRank ?? "未知";
                string flex = info.Player.FlexRank ?? "未知";

                var wins = info.WinHistory.Count(w => w);
                var total = info.WinHistory.Count;
                double winRate = total > 0 ? wins * 100.0 / total : 0;

                //sb.AppendLine($"{name}: 单双排 {solo} | 灵活 {flex} | 近20场胜率: {winRate:F1}%");

                // 拼接近10场 KDA
                string kdaString = "";
                if (info.RecentMatches != null && info.RecentMatches.Count > 0)
                {
                    var last10 = info.RecentMatches.Take(10);
                    var kdaList = last10
                        .Select(m => $"{m.Kills}/{m.Deaths}/{m.Assists}");
                    kdaString = string.Join(" ", kdaList);
                }
                else
                {
                    kdaString = "无记录";
                }

                sb.AppendLine($"{name}: 单双排 {solo} | 灵活 {flex} | 近20场胜率: {winRate:F1}% | 近10场KDA: {kdaString}");
            }

            string allMessage = sb.ToString().Trim();
            var lines = allMessage.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                Clipboard.SetText(line);

                // 打开聊天框
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(50);

                // 粘贴
                SendKeys.SendWait("^v");
                Thread.Sleep(50);

                // 回车发送
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(100);
            }

            Debug.WriteLine("[战绩信息] SendKeys 发送完成 (逐行发送)");
        }
        #endregion

        #region 显示敌方队伍卡片
        /// <summary>
        /// 创建敌方队伍卡片
        /// </summary>
        /// <returns></returns>
        private async Task ShowEnemyTeamCards()
        {
            try
            {
                Debug.WriteLine("开始执行 ShowEnemyTeamCards");

                JObject currentSummoner = await Globals.lcuClient.GetCurrentSummoner();
                if (currentSummoner?["puuid"] == null) return;

                string myPuuid = (string)currentSummoner["puuid"];
                JObject sessionData = await Globals.lcuClient.GetGameSession();

                var gameData = sessionData?["gameData"];
                var teamOne = gameData?["teamOne"] as JArray;
                var teamTwo = gameData?["teamTwo"] as JArray;
                if (teamOne == null || teamTwo == null) return;

                bool isInTeamOne = teamOne.Any(p => (string)p["puuid"] == myPuuid);
                var enemyTeam = isInTeamOne ? teamTwo : teamOne;
                int enemyRow = isInTeamOne ? 1 : 0;

                //将敌方数据存储
                _cachedEnemyTeam = enemyTeam;

                // 先创建头像占位卡片
                await CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);

                // 再异步补充段位/战绩
                //_ = FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow);
                await FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ShowEnemyTeamCards 异常: " + ex.ToString());
            }
        }
        #endregion

        #region 开始并行下载玩家历史战绩
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
            return results.ToList(); // 顺序与输入一致
        }

        private async Task<PlayerMatchInfo> SafeFetchPlayerMatchInfoAsync(JToken p, int retryTimes = 2)
        {
            for (int attempt = 1; attempt <= retryTimes + 1; attempt++)
            {
                try
                {
                    return await FetchPlayerMatchInfoAsync(p);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Fetch失败] 第 {attempt} 次尝试失败: {ex.Message}");
                    if (attempt <= retryTimes)
                        await Task.Delay(1000);
                }
            }

            Debug.WriteLine("[Fetch失败] 所有重试失败，返回 null");
            return null; // 重点：不要返回无效对象
        }

        //头像获取失败显示默认图
        private Image LoadErrorImage()
        {
            return Image.FromFile(AppDomain.CurrentDomain.BaseDirectory + "Assets\\Defaults\\Profile.png");
        }
        #endregion

        #region 下载玩家历史战绩
        /// <summary>
        /// 根据summoner信息获取玩家的puuid、段位、历史战绩
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        private async Task<PlayerMatchInfo> FetchPlayerMatchInfoAsync(JToken p)
        {
            if (p == null)
            {
                throw new ArgumentNullException("p");
            }
            long summonerId = p["summonerId"]?.Value<long>() ?? 0;
            int championId = p["championId"]?.Value<int>() ?? 0;
            if (summonerId == 0)
            {
                throw new ArgumentException("summonerId is missing or invalid.");
            }
            string championName = Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown";
            Image iconChamp = await Globals.resLoading.GetChampionIconAsync(championId);
            if (playerMatchCache.TryGetValue(summonerId, out PlayerMatchInfo cachedMatch))
            {
                cachedMatch.Player.ChampionId = championId;
                cachedMatch.Player.ChampionName = championName;
                cachedMatch.Player.Avatar = iconChamp;
                cachedMatch.IsFromCache = true;
                return cachedMatch;
            }
            string puuid = "";
            string gameName = "未知玩家";
            string privacyStatus = "[隐藏]";
            string soloRank = "未知";
            string flexRank = "未知";
            JObject summoner = await Globals.lcuClient.GetGameNameBySummonerId(summonerId.ToString());
            if (summoner != null)
            {
                puuid = summoner["puuid"]?.ToString() ?? "";
                gameName = summoner["gameName"]?.ToString() ?? "未知玩家";
                if (summoner["privacy"]?.ToString().Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    privacyStatus = "[公开]";
                }
                Dictionary<string, RankedStats> rankedStats = RankedStats.FromJson(await Globals.lcuClient.GetCurrentRankedStatsAsync(puuid));
                if (rankedStats != null)
                {
                    if (rankedStats.TryGetValue("单双排", out RankedStats soloStats))
                    {
                        soloRank = $"{soloStats.FormattedTier}({soloStats.LeaguePoints})";
                    }
                    if (rankedStats.TryGetValue("灵活组排", out RankedStats flexStats))
                    {
                        flexRank = $"{flexStats.FormattedTier}({flexStats.LeaguePoints})";
                    }
                }
            }
            PlayerInfo playerInfo = new PlayerInfo
            {
                Puuid = puuid,
                SummonerId = summonerId,
                ChampionId = championId,
                ChampionName = championName,
                Avatar = iconChamp,
                GameName = gameName,
                IsPublic = privacyStatus,
                SoloRank = soloRank,
                FlexRank = flexRank
            };
            string currGameMod = Globals.CurrGameMod;
            JArray matchesJson = await Globals.sgpClient.SgpFetchLatestMatches(puuid, 0, 20, currGameMod switch
            {
                "420" => "q_420",
                "440" => "q_440",
                "430" => "q_430",
                "450" => "q_450",
                _ => "",
            });
            if (matchesJson == null)
            {
                return null;
            }
            PlayerMatchInfo matchInfo = GetPlayerMatchInfo(puuid, matchesJson);
            matchInfo.Player = playerInfo;
            matchInfo.IsFromCache = false;
            playerMatchCache[summonerId] = matchInfo;
            return matchInfo;
        }
        #endregion

        #region 解析玩家历史战绩，并根据模式过滤战绩
        /// <summary>
        /// 监听游戏房间，根据puuid获取数据，并解析房间玩家的历史战绩数据
        /// </summary>
        /// <param name="puuid"></param>
        /// <param name="matches"></param>
        /// <returns></returns>
        public PlayerMatchInfo GetPlayerMatchInfo(string puuid, JArray matches)
        {
            var result = new PlayerMatchInfo();
            var matchItems = result.MatchItems;
            var heroIcons = result.HeroIcons;

            if (matches == null || matches.Count == 0)
            {
                Debug.WriteLine("matches 数据为空");
                return result;
            }

            try
            {
                foreach (JObject match in matches.Cast<JObject>())
                {
                    // 调试输出，查看实际的 JSON 结构
                    Debug.WriteLine($"Match JSON: {match.ToString(Newtonsoft.Json.Formatting.None)}");

                    // 根据实际的 SGP API 返回结构调整
                    // 可能的结构1: 直接包含游戏数据
                    // 可能的结构2: 包含在 "json" 字段中
                    JObject gameJson = match;

                    // 检查是否包含 "json" 字段
                    if (match["json"] != null)
                    {
                        gameJson = match["json"] as JObject;
                    }

                    if (gameJson == null)
                    {
                        Debug.WriteLine("gameJson 为空");
                        continue;
                    }

                    long gameId = gameJson["gameId"]?.Value<long>() ?? 0;
                    if (gameId == 0)
                    {
                        Debug.WriteLine("gameId 为 0");
                        continue;
                    }

                    // 查找参与者数据
                    var participants = gameJson["participants"] as JArray;
                    if (participants == null)
                    {
                        Debug.WriteLine("participants 为空");
                        continue;
                    }

                    // 查找当前玩家
                    var participant = participants.FirstOrDefault(p =>
                        p["puuid"]?.ToString() == puuid) as JObject;

                    if (participant == null)
                    {
                        Debug.WriteLine($"未找到玩家 {puuid} 的参与数据");
                        continue;
                    }

                    // 提取玩家数据
                    int teamId = participant["teamId"]?.Value<int>() ?? -1;
                    int championId = participant["championId"]?.Value<int>() ?? 0;
                    string championName = participant["championName"]?.ToString() ?? "";

                    // 获取英雄图标
                    var champion = Globals.resLoading.GetChampionById(championId);
                    string champName = champion?.Name?.Replace(" ", "").Replace("'", "") ?? championName;

                    if (!_imageCache.TryGetValue(champName, out var image))
                    {
                        image = Globals.resLoading.GetChampionIconAsync(championId).GetAwaiter().GetResult();
                        if (image != null)
                        {
                            _imageCache.TryAdd(champName, image);
                        }
                    }

                    if (image != null && !heroIcons.Images.ContainsKey(champName))
                    {
                        heroIcons.Images.Add(champName, image);
                    }

                    // 提取KDA和胜负数据
                    int kills = participant["kills"]?.Value<int>() ?? 0;
                    int deaths = participant["deaths"]?.Value<int>() ?? 0;
                    int assists = participant["assists"]?.Value<int>() ?? 0;
                    bool win = participant["win"]?.Value<bool>() ?? false;

                    // 保存到 RecentMatches
                    result.RecentMatches.Add(new MatchStat
                    {
                        Kills = kills,
                        Deaths = deaths,
                        Assists = assists
                    });

                    result.WinHistory.Add(win);

                    // 获取游戏模式
                    string gameMode = "未知模式";
                    var tags = match["metadata"]?["tags"] as JArray;
                    if (tags != null)
                    {
                        gameMode = MapGameTags(tags);
                    }
                    else
                    {
                        // 备用方案：从 queueId 获取
                        int queueId = gameJson["queueId"]?.Value<int>() ?? -1;
                        gameMode = GameMod.GetModeName(queueId, gameJson["gameMode"]?.ToString());
                    }

                    // 获取游戏时间
                    long gameStart = gameJson["gameStartTimestamp"]?.Value<long>() ?? gameJson["gameCreation"]?.Value<long>() ?? 0;
                    string gameDate = "未知";
                    if (gameStart > 0)
                    {
                        gameDate = DateTimeOffset.FromUnixTimeMilliseconds(gameStart).ToString("MM-dd");
                    }

                    // 创建列表项
                    var item = new ListViewItem
                    {
                        ImageKey = champName,
                        ForeColor = win ? Color.Green : Color.Red,
                        Tag = new MatchMetadata
                        {
                            GameId = gameId,
                            TeamId = teamId
                        }
                    };
                    item.SubItems.AddRange(new[]
                    {
                        gameMode,
                        $"{kills}/{deaths}/{assists}",
                        gameDate
                    });

                    matchItems.Add(item);
                    result.MatchKeys.Add($"{gameId}_{teamId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解析比赛数据异常: {ex.Message}");
                Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            }

            result.HeroIcons = heroIcons;
            return result;
        }

        private static string MapGameTags(JArray tags)
        {
            if (tags == null) return "未知模式";
            var tagList = tags.Select(t => t.ToString()).ToList();

            if (tagList.Contains("q_450")) return "大乱斗";
            if (tagList.Contains("q_420")) return "单双排";
            if (tagList.Contains("q_440")) return "灵活组排";
            if (tagList.Contains("q_430")) return "匹配";
            if (tagList.Contains("q_400")) return "匹配(征召)";
            if (tagList.Contains("q_830") || tagList.Contains("q_840") || tagList.Contains("q_850"))
                return "人机对战";
            if (tagList.Contains("q_900")) return "无限火力";
            if (tagList.Contains("q_1020")) return "克隆大作战";

            // fallback
            return string.Join(",", tagList);
        }

        #endregion

        #region 解析玩家历史战绩，不过滤模式

        //public PlayerMatchInfo GetPlayerMatchInfo(string puuid, JArray matches)
        //{
        //    var result = new PlayerMatchInfo();
        //    var matchItems = result.MatchItems;
        //    var heroIcons = result.HeroIcons;

        //    if (matches == null || matches.Count == 0)
        //    {
        //        return result; // 直接返回空的，避免后续异常
        //    }

        //    foreach (JObject match in matches.Cast<JObject>())
        //    {
        //        long gameId = match["gameId"]?.Value<long>() ?? 0;
        //        if (gameId == 0) continue;

        //        int currentParticipantId = match["participantIdentities"]
        //            ?.FirstOrDefault(id => id["player"]?["puuid"]?.ToString() == puuid)?["participantId"]?.Value<int>() ?? -1;
        //        if (currentParticipantId == -1) continue;

        //        var participant = match["participants"]
        //            ?.FirstOrDefault(p => p["participantId"]?.Value<int>() == currentParticipantId);
        //        if (participant == null) continue;

        //        int teamId = participant["teamId"]?.Value<int>() ?? -1;
        //        int championId = participant["championId"]?.Value<int>() ?? 0;

        //        var champion = Globals.resLoading.GetChampionById(championId);
        //        string champName = champion.Name.Replace(" ", "").Replace("'", "");

        //        if (!_imageCache.TryGetValue(champName, out var image))
        //        {
        //            // 异步加载图片，这里需要根据你的实际情况调整
        //            // 可能需要改为异步方法或预先加载所有图片
        //            image = Globals.resLoading.GetChampionIconAsync(championId).GetAwaiter().GetResult();
        //            if (image != null)
        //            {
        //                _imageCache.TryAdd(champName, image);
        //            }
        //        }

        //        if (image != null && !heroIcons.Images.ContainsKey(champName))
        //        {
        //            heroIcons.Images.Add(champName, image);
        //        }

        //        var stats = participant["stats"];
        //        int kills = stats?["kills"]?.Value<int>() ?? 0;
        //        int deaths = stats?["deaths"]?.Value<int>() ?? 0;
        //        int assists = stats?["assists"]?.Value<int>() ?? 0;
        //        bool win = stats?["win"]?.Value<bool>() ?? false;

        //        // 保存到 RecentMatches 用来发送kda信息
        //        result.RecentMatches.Add(new MatchStat
        //        {
        //            Kills = kills,
        //            Deaths = deaths,
        //            Assists = assists
        //        });

        //        // 新增
        //        result.WinHistory.Add(win);

        //        string gameMode = GameMod.GetModeName(
        //                match["queueId"]?.Value<int>() ?? -1,
        //                match["gameMode"]?.ToString()
        //            );

        //        var item = new ListViewItem
        //        {
        //            ImageKey = champName,
        //            ForeColor = win ? Color.Green : Color.Red,
        //            Tag = new MatchMetadata
        //            {
        //                GameId = gameId,
        //                TeamId = teamId
        //            }
        //        };
        //        item.SubItems.AddRange(new[]
        //        {
        //            gameMode,
        //            $"{kills}/{deaths}/{assists}",
        //            DateTimeOffset.FromUnixTimeMilliseconds(match["gameCreation"].Value<long>()).ToString("MM-dd")
        //        });

        //        matchItems.Add(item);

        //        // 加入匹配用的key：gameId_teamId 代表“同一场、同一队伍”
        //        result.MatchKeys.Add($"{gameId}_{teamId}");
        //    }

        //    result.HeroIcons = heroIcons;
        //    return result;
        //}

        #endregion
        
        #region UI更新处理

        /// <summary>
        /// 只更新与队伍相同的玩家颜色
        /// </summary>
        /// <param name="summonerId"></param>
        /// <param name="color"></param>
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

        /// <summary>
        /// 用来判断是否房间里面切换了英雄，如果只切换英雄则更新英雄头像
        /// </summary>
        /// <param name="matchInfo"></param>
        /// <param name="isMyTeam"></param>
        /// <param name="row"></param>
        /// <param name="column"></param>
        private void UpdateOrCreateLoadingPlayerMatch(PlayerMatchInfo matchInfo, bool isMyTeam, int row, int column)
        {
            var player = matchInfo.Player;
            var key = (row, column);

            long summonerId = player.SummonerId;
            int championId = player.ChampionId;

            if (playerCache.TryGetValue(key, out var cached))
            {
                if (cached.summonerId == summonerId &&
                    cached.championId == championId)
                {
                    // 若 UI 当前是加载中，则刷新
                    var panel = tableLayoutPanel1.GetControlFromPosition(column, row) as BorderPanel;
                    var card = panel?.Controls.Count > 0 ? panel.Controls[0] as PlayerCardControl : null;

                    if (card != null && card.IsLoading)
                    {
                        Debug.WriteLine($"[刷新加载中卡片] summonerId={summonerId}");
                        CreateLoadingPlayerMatch(matchInfo, isMyTeam, row, column);
                    }

                    return;
                }

                if (cached.summonerId == summonerId && cached.championId != championId)
                {
                    UpdatePlayerAvatarOnly(row, column, player);
                    playerCache[key] = (summonerId, championId);
                    return;
                }
            }

            // 玩家或英雄完全变更，直接重建卡片
            CreateLoadingPlayerMatch(matchInfo, isMyTeam, row, column);
            playerCache[key] = (summonerId, championId);
        }

        /// <summary>
        /// 更新英雄头像方法
        /// </summary>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <param name="player"></param>
        private void UpdatePlayerAvatarOnly(int row, int column, PlayerInfo player)
        {
            SafeInvoke(tableLayoutPanel1, () =>
            {
                var panel = tableLayoutPanel1.GetControlFromPosition(column, row) as BorderPanel;
                if (panel != null && panel.Controls.Count > 0)
                {
                    var card = panel.Controls[0] as PlayerCardControl;
                    if (card != null)
                    {
                        card.SetAvatarOnly(player.Avatar); // 只更新头像，不碰列表
                    }
                }
            });
        }


        /// <summary>
        /// 根据获取到的敌我双方数据创建卡片，并更新UI显示
        /// </summary>
        /// <param name="matchInfo"></param>
        /// <param name="isMyTeam"></param>
        /// <param name="row"></param>
        /// <param name="column"></param>
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

            // 注册映射，便于之后只更新颜色
            _cardBySummonerId[matchInfo.Player.SummonerId] = card;

            string name = player.GameName ?? "未知";
            string soloRank = string.IsNullOrEmpty(player.SoloRank) ? "未知" : player.SoloRank;
            string flexRank = string.IsNullOrEmpty(player.FlexRank) ? "未知" : player.FlexRank;

            Color nameColor = matchInfo.Player.NameColor;
            card.SetPlayerInfo(name, soloRank, flexRank, player.Avatar, player.IsPublic, matchItems, nameColor);
            card.ListViewControl.SmallImageList = heroIcons;
            card.ListViewControl.View = View.Details;

            panel.Controls.Add(card);

            // 加入控件前先移除旧的
            SafeInvoke(tableLayoutPanel1, () =>
            {
                var oldControl = tableLayoutPanel1.GetControlFromPosition(column, row);
                if (oldControl != null)
                {
                    tableLayoutPanel1.Controls.Remove(oldControl);
                    oldControl.Dispose(); // 释放旧控件资源
                }

                tableLayoutPanel1.Controls.Add(panel, column, row);
            });
        }


        public static void SafeInvoke(Control control, Action action)
        {
            if (control.IsDisposed) return;

            if (control.InvokeRequired)
            {
                try
                {
                    control.BeginInvoke(action); // 异步，不阻塞调用线程
                }
                catch
                {
                    // 控件已销毁时忽略
                }
            }
            else
            {
                action();
            }
        }
        #endregion

        #region 查询战绩与解析
        /// <summary>
        /// 查询数据
        /// </summary>
        /// <param name="puuid"></param>
        /// <param name="begIndex"></param>
        /// <param name="endIndex"></param>
        /// <param name="queueId"></param>
        /// <returns></returns>
        public async Task<JArray> LoadFullMatchDataAsync(string puuid, int begIndex, int pageSize, string queueId = null, bool? forceRefresh = null) // 改成可空
        {
            bool refreshFlag = forceRefresh ?? RefreshState.ForceMatchRefresh;

            if (refreshFlag)
                Debug.WriteLine("⚡ 强制刷新已启用");

            // 1️⃣ 尝试从缓存读取
            if (!refreshFlag)
            {
                var cached = MatchCache.Get(puuid, queueId, begIndex, pageSize);
                if (cached != null)
                {
                    Debug.WriteLine($"从缓存读取: {puuid} [{queueId ?? "all"}] 起点{begIndex} 每页{pageSize}");
                    return cached;
                }
            }

            // 2️⃣ 发起网络请求
            var allGames = await Globals.sgpClient.SgpFetchLatestMatches(puuid, begIndex, pageSize, queueId);

            // 4️⃣ 写入缓存
            MatchCache.Add(puuid, queueId, begIndex, pageSize, allGames);

            // 5️⃣ 重置强制刷新状态（只生效一次）
            RefreshState.ForceMatchRefresh = false;
            return allGames;
        }

        /// <summary>
        /// 数据解析
        /// </summary>
        /// <param name="gameObj"></param>
        /// <param name="gameName"></param>
        /// <param name="tagLine"></param>
        /// <returns></returns>
        public async Task<Panel> ParseGameToPanelAsync(JObject gameObj, string gameName, string tagLine, int index)
        {
            MatchParserSgp _parser = new MatchParserSgp();
            _parser.PlayerIconClicked += Panel_PlayerIconClicked;
            Panel panel = await _parser.ParseGameToPanelFromSgpAsync(gameObj, gameName, tagLine, index);
            MatchListPanel matchPanel = panel as MatchListPanel;
            if (matchPanel != null)
            {
                matchPanel.DetailsClicked += delegate (MatchInfo matchInfo)
                {
                    try
                    {
                        Debug.WriteLine("详情按钮被点击，模式：" + matchInfo.QueueId + "，开始显示详情");
                        if (IsSummonersRiftOrAram(matchInfo.QueueId))
                        {
                            ShowMatchDetails(matchInfo);
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
                    try
                    {
                        if (!IsReplaySupported(matchInfo.QueueId))
                        {
                            MessageBox.Show("该模式不支持查看回放！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Question);
                        }
                        else if (!matchInfo.IsReplayDownloaded)
                        {
                            DialogResult result = MessageBox.Show($"点击确定将自动下载回放文件并开始播放比赛！\n比赛ID: {matchInfo.GameId}\n模式: {matchInfo.Mode}", "查看回放", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                            if (result == DialogResult.OK)
                            {
                                if (await Globals.lcuClient.DownloadReplayAsync(matchInfo.GameId))
                                {
                                    matchInfo.IsReplayDownloaded = true;
                                    matchPanel.Invalidate();
                                    if (!(await Globals.lcuClient.PlayReplayAsync(matchInfo.GameId)))
                                    {
                                        MessageBox.Show("回放下载完成，但启动播放失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                    }
                                }
                                else
                                {
                                    MessageBox.Show("下载失败，请检查是否为最近比赛或支持回放的模式。", "下载失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                }
                            }
                        }
                        else if (await Globals.lcuClient.PlayReplayAsync(matchInfo.GameId))
                        {
                            MessageBox.Show("回放启动成功！", "播放成功", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                        }
                        else
                        {
                            MessageBox.Show("回放启动失败", "播放失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                        }
                    }
                    catch (Exception ex)
                    {
                        Exception ex2 = ex;
                        MessageBox.Show("处理回放失败: " + ex2.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                    }
                };
            }
            return panel;
            static bool IsReplaySupported(string queueTag)
            {
                string[] replayAllowed = new string[12]
                {
                    "q_400", "q_420", "q_430", "q_440", "q_450", "q_830", "q_840", "q_850", "q_900", "q_1010",
                    "q_1020", "q_1900"
                };
                return replayAllowed.Contains(queueTag);
            }
            static bool IsSummonersRiftOrAram(string queueTag)
            {
                string[] allowed = new string[12]
                {
                    "q_400", "q_420", "q_430", "q_440", "q_450", "q_830", "q_840", "q_850", "q_900", "q_1010",
                    "q_1020", "q_1900"
                };
                return allowed.Contains(queueTag);
            }
        }
        #endregion

        /// <summary>
        /// 显示比赛详情（重构版）
        /// 在ShowMatchDetails方法中初始化
        /// </summary>
        private void ShowMatchDetails(MatchInfo matchInfo)
        {
            Form detailForm = null;
            ToolTip toolTip = null;
            try
            {
                Debug.WriteLine("显示比赛详情: " + matchInfo.SelfPlayer.GameName + " - " + matchInfo.GameTime);
                if (matchInfo?.AllParticipants == null || matchInfo.AllParticipants.Count == 0)
                {
                    MessageBox.Show("比赛数据不完整，无法显示详情");
                    return;
                }
                detailForm = new Form
                {
                    Text = "比赛详情 - " + matchInfo.ChampionName + " - " + matchInfo.GameTime,
                    Size = new Size(1300, 800),
                    StartPosition = FormStartPosition.CenterScreen,
                    BackColor = Color.White
                };
                toolTip = new ToolTip
                {
                    AutoPopDelay = 30000,
                    InitialDelay = 100,
                    ReshowDelay = 50,
                    ShowAlways = true,
                    UseAnimation = true,
                    UseFading = true
                };
                DetailedViewLayoutManager detailedViewLayoutManager = new DetailedViewLayoutManager(toolTip);
                TabControl tabControl = new TabControl
                {
                    Dock = DockStyle.Fill,
                    Font = new Font("微软雅黑", 9f)
                };
                TabPage detailedViewTab = new TabPage("详细视图");
                detailedViewTab.BackColor = Color.White;
                tabControl.TabPages.Add(detailedViewTab);
                detailForm.Controls.Add(tabControl);
                detailForm.FormClosed += delegate
                {
                    try
                    {
                        toolTip?.RemoveAll();
                        toolTip?.Dispose();
                        DisposeImagesInControls(detailForm.Controls);
                    }
                    catch (Exception ex2)
                    {
                        Debug.WriteLine("释放资源时出错: " + ex2.Message);
                    }
                };
                InitializeDetailedView(detailedViewTab, matchInfo, detailedViewLayoutManager);
                detailForm.ShowDialog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("显示详情失败: " + ex.Message + "\n" + ex.StackTrace);
                MessageBox.Show("显示详情失败: " + ex.Message);
                toolTip?.Dispose();
                detailForm?.Dispose();
            }
        }

        private void DisposeImagesInControls(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                if (control is PictureBox pictureBox && pictureBox.Image != null)
                {
                    try
                    {
                        pictureBox.Image.Dispose();
                        pictureBox.Image = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("释放图片资源失败: " + ex.Message);
                    }
                }
                if (control.HasChildren)
                {
                    DisposeImagesInControls(control.Controls);
                }
            }
        }

        private void InitializeDetailedView(TabPage tabPage, MatchInfo matchInfo, DetailedViewLayoutManager layoutManager)
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.White
            };
            CreateDetailedPlayerViews(panel, matchInfo, layoutManager);
            tabPage.Controls.Add(panel);
        }

        /// <summary>
        /// 创建详细玩家视图
        /// </summary>
        private async void CreateDetailedPlayerViews(Panel panel, MatchInfo matchInfo, DetailedViewLayoutManager layoutManager)
        {
            List<JObject> blueTeam = matchInfo.AllParticipants.Where(delegate (JObject p)
            {
                JToken? jToken = p["teamId"];
                return jToken != null && jToken.Value<int>() == 100;
            }).ToList();
            List<JObject> redTeam = matchInfo.AllParticipants.Where(delegate (JObject p)
            {
                JToken? jToken = p["teamId"];
                return jToken != null && jToken.Value<int>() == 200;
            }).ToList();
            int y = 10;
            Label blueLabel = new Label
            {
                Text = "\ud83d\udd35 蓝队 (100)",
                Location = new Point(10, y),
                Size = new Size(200, 25),
                Font = new Font("微软雅黑", 11f, FontStyle.Bold),
                ForeColor = Color.DodgerBlue
            };
            panel.Controls.Add(blueLabel);
            y += 30;
            foreach (JObject participant in blueTeam)
            {
                Panel playerPanel = await layoutManager.CreateDetailedPlayerPanel(participant, matchInfo, 20, y, 100);
                panel.Controls.Add(playerPanel);
                y += playerPanel.Height + 10;
            }
            y += 20;
            Label redLabel = new Label
            {
                Text = "\ud83d\udd34 红队 (200)",
                Location = new Point(10, y),
                Size = new Size(200, 25),
                Font = new Font("微软雅黑", 11f, FontStyle.Bold),
                ForeColor = Color.Red
            };
            panel.Controls.Add(redLabel);
            y += 30;
            foreach (JObject participant2 in redTeam)
            {
                Panel playerPanel2 = await layoutManager.CreateDetailedPlayerPanel(participant2, matchInfo, 20, y, 200);
                panel.Controls.Add(playerPanel2);
                y += playerPanel2.Height + 10;
            }
        }


        /// <summary>
        /// 绑定玩家头像按钮事件
        /// </summary>
        /// <param name="fullName"></param>
        private async void Panel_PlayerIconClicked(string summonerId)
        {
            ShowLoadingIndicator();
            try
            {
                JObject summoner = await Globals.lcuClient.GetGameNameBySummonerId(summonerId);
                if (summoner != null)
                {
                    RefreshState.ForceMatchRefresh = true;
                    Dictionary<string, RankedStats> rankedStats = RankedStats.FromJson(await Globals.lcuClient.GetCurrentRankedStatsAsync(summoner["puuid"].ToString()));
                    string privacyStatus = "隐藏";
                    if (summoner["privacy"]?.ToString().Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) ?? false)
                    {
                        privacyStatus = "公开";
                    }
                    _matchTabContent.CreateNewTab(summoner["gameName"].ToString(), summoner["tagLine"].ToString(), summoner["puuid"].ToString(), summoner["profileIconId"].ToString(), summoner["summonerLevel"].ToString(), privacyStatus, rankedStats);
                    txtGameName.Text = "";
                }
            }
            catch (Exception ex)
            {
                Exception ex2 = ex;
                MessageBox.Show("查询失败: " + ex2.Message);
            }
            finally
            {
                HideLoadingIndicator();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_matchTabContent != null)
            {
                _matchTabContent.CleanupAllTabs();
            }
            _watcherCts?.Cancel();
            _champSelectCts?.Cancel();
            _lcuPoller?.Stop();
            _tab1Poller?.Stop();
            base.OnFormClosing(e);
        }

        #region LCU 检测连接提示
        private void SetLcuUiState(bool connected, bool inGame)
        {
            if (!connected)
            {
                SafeInvoke(panelMatchList, () =>
                {
                    ShowLcuNotConnectedMessage(panelMatchList);
                });
                SafeInvoke(penalGameMatchData, () =>
                {
                    ShowLcuNotConnectedMessage(penalGameMatchData);
                });
            }
            else if (!_isGame)
            {
                SafeInvoke(penalGameMatchData, () =>
                {
                    ShowWaitingForGameMessage(penalGameMatchData);
                });
            }
        }

        private Panel CreateStatusPanel(string message, bool showLolLauncher = false)
        {
            Panel containerPanel = new Panel
            {
                Width = 500,
                Height = 200,
                BackColor = Color.Transparent
            };
            Label label = new Label
            {
                Text = message,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 50,
                Font = new Font("微软雅黑", 12f, FontStyle.Bold)
            };
            ProgressBar progress = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Width = 200,
                Height = 30,
                MarqueeAnimationSpeed = 30
            };
            progress.Left = (containerPanel.Width - progress.Width) / 2;
            progress.Top = label.Bottom + 10;
            containerPanel.Controls.Add(label);
            containerPanel.Controls.Add(progress);
            if (showLolLauncher)
            {
                LOLHelper helper = new LOLHelper();
                string exePath = helper.GetLOLLoginExePath();
                LinkLabel linkLolPath = new LinkLabel
                {
                    AutoSize = true,
                    Text = (string.IsNullOrEmpty(exePath) ? "未检测到 LOL 登录程序" : exePath),
                    Font = new Font("微软雅黑", 10f, FontStyle.Regular)
                };
                Button btnStartLol = new Button
                {
                    Text = "启动LOL登录程序",
                    Width = 200,
                    Height = 30
                };
                linkLolPath.Left = (containerPanel.Width - linkLolPath.PreferredWidth) / 2;
                linkLolPath.Top = progress.Bottom + 30;
                btnStartLol.Left = (containerPanel.Width - btnStartLol.Width) / 2;
                btnStartLol.Top = linkLolPath.Bottom + 10;
                containerPanel.Controls.Add(linkLolPath);
                containerPanel.Controls.Add(btnStartLol);
                if (!string.IsNullOrEmpty(exePath))
                {
                    linkLolPath.LinkClicked += delegate
                    {
                        string directoryName = Path.GetDirectoryName(exePath);
                        if (Directory.Exists(directoryName))
                        {
                            Process.Start("explorer.exe", directoryName);
                        }
                    };
                    btnStartLol.Click += delegate
                    {
                        linkLolPath.Text = exePath;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            Debug.WriteLine("找到 LOL 登录程序：" + exePath);
                            helper.StartLOLLoginProgram(exePath);
                        }
                        else
                        {
                            Debug.WriteLine("未检测到 LOL 登录程序！");
                        }
                    };
                }
            }
            return containerPanel;
        }


        private void ShowLcuNotConnectedMessage(Control parentControl)
        {
            parentControl.Controls.Clear();

            var panel = CreateStatusPanel(
                "正在检测客户端连接，请确保登录了游戏...",
                showLolLauncher: true
            );

            panel.Left = (parentControl.Width - panel.Width) / 2;
            panel.Top = (parentControl.Height - panel.Height) / 2;
            panel.Anchor = AnchorStyles.None;

            parentControl.Controls.Add(panel);
        }


        private void ShowWaitingForGameMessage(Control parentControl)
        {
            parentControl.Controls.Clear();   // 新增：清理所有旧控件

            tableLayoutPanel1.Visible = false;

            _waitingPanel = CreateStatusPanel("正在等待加入游戏，请稍后...", showLolLauncher: false);

            _waitingPanel.Left = (parentControl.Width - _waitingPanel.Width) / 2;
            _waitingPanel.Top = (parentControl.Height - _waitingPanel.Height) / 2;
            _waitingPanel.Anchor = AnchorStyles.None;

            parentControl.Controls.Add(_waitingPanel);
        }

        private void imageTabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            int selectedTabIndex = imageTabControl1.SelectedIndex;

            switch (selectedTabIndex)
            {
                case 0:
                    _tab1Poller.Stop();
                    break;

                case 1:
                    StartTab1Polling();
                    break;

                case 2:
                    _tab1Poller.Stop();
                    Debug.WriteLine("自动化设置");
                    break;
            }
        }

        private void StartTab1Polling()
        {
            _tab1Poller.Start(async () =>
            {
                try
                {
                    SetLcuUiState(lcuReady, _isGame);
                }
                catch (TaskCanceledException)
                {
                    // 忽略
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Tab1Poller轮询异常: {ex}");
                }
            }, 3000);
        }

        #endregion
    }
}