using League.Extensions;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    public class GameChatSender
    {
        private readonly FormMain _mainForm;
        private readonly ChatMessageBuilder? _chatMessageBuilder;
        private bool _isSendingMessage = false;

        public GameChatSender(FormMain mainForm, ChatMessageBuilder? chatMessageBuilder)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            _chatMessageBuilder = chatMessageBuilder;
        }

        // 🔥 Ctrl + F7 - 选人阶段发送（保持不变）
        public async Task HandleChampSelectF7Async()
        {
            string phase = await GetGameflowPhaseSafeAsync();
            if (phase != "ChampSelect")
            {
                Debug.WriteLine($"[Ctrl+F7] 当前不是选人阶段: {phase}");
                return;
            }

            var (isCustomMode, msg) = await GetUiSendStateAsync();

            if (!isCustomMode)
            {
                var myTeam = _mainForm._cachedMyTeam;
                if (myTeam == null || myTeam.Count == 0)
                {
                    Debug.WriteLine("[Ctrl+F7] 缓存为空，尝试实时获取");
                    var session = await Globals.lcuClient.GetChampSelectSession();
                    if (session != null) _mainForm._cachedMyTeam = session["myTeam"] as JArray;
                    myTeam = _mainForm._cachedMyTeam;
                }

                if (myTeam == null || myTeam.Count == 0)
                {
                    Debug.WriteLine("[Ctrl+F7] 仍无法获取我方队伍数据");
                    return;
                }
                msg = _chatMessageBuilder?.BuildMyTeamSummary(myTeam) ?? "";
            }

            await SendMessageAsync(msg, isChampSelect: true);
        }

        // 🔥 F9 - 发送我方队伍
        public async Task HandleMyTeamAsync()
        {
            if (_isSendingMessage) return;

            string phase = await GetGameflowPhaseSafeAsync();
            if (phase != "InProgress")
            {
                Debug.WriteLine($"[F9] 非游戏阶段: {phase}");
                return;
            }

            var (isCustomMode, msg) = await GetUiSendStateAsync();

            if (!isCustomMode)
            {
                var myTeam = _mainForm._cachedMyTeam;
                if (myTeam == null || myTeam.Count == 0)
                {
                    Debug.WriteLine("[F9] 缓存为空，尝试从Session获取");
                    var session = await Globals.lcuClient.GetChampSelectSession();
                    if (session != null) _mainForm._cachedMyTeam = session["myTeam"] as JArray;
                    myTeam = _mainForm._cachedMyTeam;
                }

                if (myTeam == null || myTeam.Count == 0)
                {
                    Debug.WriteLine("[F9] 无法获取我方队伍");
                    return;
                }
                msg = _chatMessageBuilder?.BuildMyTeamSummary(myTeam) ?? "";
            }

            if (string.IsNullOrWhiteSpace(msg)) return;

            try
            {
                _isSendingMessage = true;
                await SendMessageAsync(msg, isChampSelect: false);
            }
            finally
            {
                _isSendingMessage = false;
            }
        }

        // 🔥 F11 - 发送全队信息（原F12功能）
        public async Task HandleFullTeamAsync()
        {
            string phase = await GetGameflowPhaseSafeAsync();
            if (phase != "InProgress")
            {
                Debug.WriteLine($"[F11] 非游戏阶段: {phase}");
                return;
            }

            var (isCustomMode, msg) = await GetUiSendStateAsync();

            if (!isCustomMode)
            {
                var myTeam = _mainForm._cachedMyTeam;
                var enemyTeam = _mainForm._cachedEnemyTeam;

                if (myTeam?.Count == 0 || enemyTeam?.Count == 0)
                {
                    Debug.WriteLine("[F11] 缓存为空，尝试从GameSession获取");
                    var session = await Globals.lcuClient.GetGameSession();
                    if (session != null)
                    {
                        myTeam = session["gameData"]?["teamOne"] as JArray;
                        enemyTeam = session["gameData"]?["teamTwo"] as JArray;
                        _mainForm._cachedMyTeam = myTeam;
                        _mainForm._cachedEnemyTeam = enemyTeam;
                    }
                }

                if (myTeam?.Count > 0 != true || enemyTeam?.Count > 0 != true)
                {
                    Debug.WriteLine("[F11] 仍无法获取队伍数据");
                    return;
                }

                msg = _chatMessageBuilder?.BuildFullTeamSummary(myTeam, enemyTeam) ?? "";
            }

            await SendMessageAsync(msg, isChampSelect: false);
        }

        /// <summary>
        /// 从 UI 线程安全获取发送模式和消息内容
        /// </summary>
        private async Task<(bool isCustomMode, string msg)> GetUiSendStateAsync()
        {
            bool isCustomMode = false;
            string msg = "";

            await _mainForm.InvokeIfRequiredAsync(async () =>
            {
                isCustomMode = _mainForm.GetSendMode();
                msg = _mainForm.GetCustomSendContent();
            });

            return (isCustomMode, msg);
        }

        private async Task SendMessageAsync(string msg, bool isChampSelect)
        {
            if (string.IsNullOrWhiteSpace(msg))
            {
                Debug.WriteLine("[消息发送] 发送内容为空，取消发送");
                return;
            }

            bool success = isChampSelect
                ? await Globals.lcuClient.SendChampSelectMessageAsync(msg)
                : await Globals.lcuClient.SendInGameMessageAsync(msg);

            string type = isChampSelect ? "选人阶段" : "游戏内";
            Debug.WriteLine($"[消息发送] {type} 发送 {(success ? "成功" : "失败")}");
        }

        private async Task<string> GetGameflowPhaseSafeAsync()
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
    }
}