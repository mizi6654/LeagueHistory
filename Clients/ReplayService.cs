using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace League.Clients
{
    /// <summary>
    /// 游戏回放服务
    /// </summary>
    public class ReplayService
    {
        private readonly LcuClient _client;
        private const int DownloadTimeoutMs = 90000;
        private const int CheckIntervalMs = 2000;

        public ReplayService(LcuClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// 下载游戏回放
        /// </summary>
        public async Task<bool> DownloadReplayAsync(long gameId, string contextData = "match-history")
        {
            try
            {
                Debug.WriteLine($"[回放] 开始下载回放，GameId: {gameId}");

                // 检查是否已存在有效回放
                var existingCheck = await CheckExistingReplayAsync(gameId);
                if (existingCheck.exists && existingCheck.isValid)
                {
                    Debug.WriteLine("[回放] 回放已存在且有效，跳过下载");
                    return true;
                }

                // 发送下载请求
                var jsonObj = new JObject { ["contextData"] = contextData };
                var postContent = new StringContent(jsonObj.ToString(), Encoding.UTF8, "application/json");

                var response = await _client.PostAsync($"/lol-replays/v1/rofls/{gameId}/download", postContent);
                Debug.WriteLine($"[回放] 下载请求响应: {response.StatusCode}");

                if (!response.IsSuccessStatusCode &&
                    response.StatusCode != HttpStatusCode.NoContent &&
                    response.StatusCode != HttpStatusCode.Accepted)
                {
                    Debug.WriteLine("[回放] 下载请求失败");
                    return false;
                }

                // 等待下载完成
                return await WaitForDownloadCompleteAsync(gameId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[回放] 下载异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 播放游戏回放
        /// </summary>
        public async Task<bool> PlayReplayAsync(long gameId, string contextData = "match-history")
        {
            try
            {
                Debug.WriteLine($"[回放] 尝试播放回放，GameId: {gameId}");

                var jsonObj = new JObject { ["contextData"] = contextData };
                var postContent = new StringContent(jsonObj.ToString(), Encoding.UTF8, "application/json");

                var response = await _client.PostAsync($"/lol-replays/v1/rofls/{gameId}/watch", postContent);

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[回放] 播放请求成功");
                    return true;
                }

                var respText = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[回放] 播放失败: {response.StatusCode} {respText}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[回放] 播放异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 检查回放元数据
        /// </summary>
        private async Task<(bool exists, bool isValid)> CheckExistingReplayAsync(long gameId)
        {
            try
            {
                var response = await _client.GetAsync($"/lol-replays/v1/metadata/{gameId}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var metaJson = JObject.Parse(content);

                    var state = metaJson["state"]?.ToString()?.ToLowerInvariant() ?? "";
                    var progress = metaJson["downloadProgress"]?.Value<int>() ?? 0;

                    Debug.WriteLine($"[回放] 现有metadata状态: {state}, 进度: {progress}%");

                    return (exists: true, isValid: state == "watch" && progress >= 100);
                }

                return (exists: false, isValid: false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[回放] 检查现有回放异常: {ex.Message}");
                return (exists: false, isValid: false);
            }
        }

        /// <summary>
        /// 等待下载完成
        /// </summary>
        private async Task<bool> WaitForDownloadCompleteAsync(long gameId)
        {
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < DownloadTimeoutMs)
            {
                await Task.Delay(CheckIntervalMs);

                try
                {
                    var response = await _client.GetAsync($"/lol-replays/v1/metadata/{gameId}");

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var metaJson = JObject.Parse(content);

                        var state = metaJson["state"]?.ToString()?.ToLowerInvariant() ?? "";
                        var progress = metaJson["downloadProgress"]?.Value<int>() ?? 0;

                        Debug.WriteLine($"[回放] 下载状态: {state}, 进度: {progress}%");

                        if (state == "watch" && progress >= 100)
                        {
                            Debug.WriteLine("[回放] 下载完成");
                            return true;
                        }

                        if (state == "failed" || state == "error")
                        {
                            Debug.WriteLine("[回放] 下载失败");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[回放] 状态查询异常: {ex.Message}");
                }
            }

            Debug.WriteLine("[回放] 下载超时");
            return false;
        }
    }
}