using League.Extensions;
using League.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    public class GameChatSender
    {
        private readonly FormMain _mainForm;
        private ChatMessageBuilder? _chatMessageBuilder;
        private bool _isSendingMessage = false;

        private const int MaxWaitMs = 1000;
        private const int RetryIntervalMs = 350;

        public GameChatSender(FormMain mainForm, ChatMessageBuilder? chatMessageBuilder)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            _chatMessageBuilder = chatMessageBuilder;
        }

        public void SetChatMessageBuilder(ChatMessageBuilder chatMessageBuilder)
        {
            _chatMessageBuilder = chatMessageBuilder ?? throw new ArgumentNullException(nameof(chatMessageBuilder));
            Debug.WriteLine("[GameChatSender] ChatMessageBuilder 已成功设置");
        }

        // Ctrl + F7 - 选人阶段
        public async Task HandleChampSelectF7Async()
        {
            string phase = await GetGameflowPhaseSafeAsync();
            if (phase != "ChampSelect") return;

            var (isCustomMode, msg) = await GetUiSendStateAsync();

            if (!isCustomMode)
            {
                var myTeam = _mainForm._cachedMyTeam;
                if (myTeam == null || myTeam.Count == 0)
                {
                    var session = await Globals.lcuClient.GetChampSelectSession();
                    if (session != null) _mainForm._cachedMyTeam = session["myTeam"] as JArray;
                    myTeam = _mainForm._cachedMyTeam;
                }

                if (myTeam == null || myTeam.Count == 0) return;

                // 选人阶段强制使用玩家名称（英雄未锁定）
                msg = _chatMessageBuilder?.BuildMyTeamSummary(myTeam, useChampionName: false) ?? "";
            }

            await SendMessageAsync(msg, isChampSelect: true);
        }

        public async Task HandleMyTeamAsync()
        {
            if (_isSendingMessage) return;
            await SendTeamDataAsync(isFullTeam: false);
        }

        public async Task HandleFullTeamAsync()
        {
            await SendTeamDataAsync(isFullTeam: true);
        }

        private async Task SendTeamDataAsync(bool isFullTeam)
        {
            if (_isSendingMessage) return;

            string phase = await GetGameflowPhaseSafeAsync();
            if (phase != "InProgress") return;

            var (isCustomMode, msg) = await GetUiSendStateAsync();
            if (isCustomMode)
            {
                await SendMessageAsync(msg, false);
                return;
            }

            var config = _mainForm.GetAppConfig();
            bool useChampionName = config?.UseChampionNameWhenSending ?? true;

            JArray? myTeam = await EnsureTeamDataReadyAsync(_mainForm._cachedMyTeam, true);
            JArray? enemyTeam = isFullTeam ? await EnsureTeamDataReadyAsync(_mainForm._cachedEnemyTeam, false) : null;

            if (myTeam?.Count == 0 || (isFullTeam && enemyTeam?.Count == 0))
                return;

            msg = isFullTeam
                ? _chatMessageBuilder?.BuildFullTeamSummary(myTeam, enemyTeam, useChampionName) ?? ""
                : _chatMessageBuilder?.BuildMyTeamSummary(myTeam, useChampionName) ?? "";

            if (string.IsNullOrWhiteSpace(msg)) return;

            try
            {
                _isSendingMessage = true;
                await SendMessageAsync(msg, false);
            }
            finally
            {
                _isSendingMessage = false;
            }
        }

        private async Task<JArray?> EnsureTeamDataReadyAsync(JArray? team, bool isMyTeam)
        {
            if (team == null || team.Count == 0)
            {
                var session = await Globals.lcuClient.GetGameSession();
                if (session != null)
                {
                    team = isMyTeam ? session["gameData"]?["teamOne"] as JArray : session["gameData"]?["teamTwo"] as JArray;
                }
            }

            if (team == null || team.Count == 0) return null;

            var start = DateTime.Now;
            int attempt = 0;
            int readyCount = 0;

            while ((DateTime.Now - start).TotalMilliseconds < MaxWaitMs)
            {
                attempt++;
                var infos = _mainForm._playerCardManager?.GetAllCachedPlayerInfos() ?? new Dictionary<long, PlayerMatchInfo>();

                readyCount = 0;
                foreach (var p in team)
                {
                    long sid = p["summonerId"]?.Value<long>() ?? 0;
                    if (sid != 0 && infos.ContainsKey(sid))
                        readyCount++;
                }

                if (readyCount == team.Count)
                {
                    Debug.WriteLine($"[Ensure] {(isMyTeam ? "我方" : "敌方")} 数据完全就绪！");
                    return team;
                }

                if (attempt % 6 == 0)
                    Debug.WriteLine($"[Ensure] {(isMyTeam ? "我方" : "敌方")} 等待中... {readyCount}/{team.Count}");

                await Task.Delay(RetryIntervalMs);
            }

            Debug.WriteLine($"[Ensure] {(isMyTeam ? "我方" : "敌方")} 等待超时，最终就绪 {readyCount}/{team.Count}");
            return team;
        }

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
            if (string.IsNullOrWhiteSpace(msg)) return;

            bool success = isChampSelect
                ? await Globals.lcuClient.SendChampSelectMessageAsync(msg)
                : await Globals.lcuClient.SendInGameMessageAsync(msg);

            Debug.WriteLine($"[消息发送] {(isChampSelect ? "选人阶段" : "游戏内")} 发送 {(success ? "成功" : "失败")}");
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