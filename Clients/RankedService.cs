using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace League.Clients
{
    /// <summary>
    /// 排位信息服务
    /// </summary>
    public class RankedService
    {
        private readonly LcuClient _client;

        public RankedService(LcuClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// 获取排位统计数据
        /// </summary>
        public async Task<JObject> GetCurrentRankedStatsAsync(string puuid)
        {
            try
            {
                var response = await _client.GetAsync($"/lol-ranked/v1/ranked-stats/{puuid}");
                var content = await response.Content.ReadAsStringAsync();
                return JObject.Parse(content);
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"[LCU] 请求超时: {ex.Message}");
                return new JObject();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取段位失败: {ex}");
                return new JObject();
            }
        }
    }
}