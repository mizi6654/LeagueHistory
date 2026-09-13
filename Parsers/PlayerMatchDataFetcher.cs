using League.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Parsers
{
    /// <summary>
    /// 负责所有外部数据获取（LCU / SGP 调用）
    /// </summary>
    public class PlayerMatchDataFetcher
    {
        public async Task<JObject> GetGameNameBySummonerIdAsync(string summonerId)
        {
            return await Globals.lcuClient.GetGameNameBySummonerId(summonerId);
        }

        public async Task<Dictionary<string, RankedStats>> GetRankedStatsAsync(string puuid)
        {
            var rankedJson = await Globals.lcuClient.GetCurrentRankedStatsAsync(puuid);
            return RankedStats.FromJson(rankedJson);
        }

        /// <summary>
        /// 选人窗口卡片战绩查询数量配置
        /// </summary>
        /// <param name="puuid"></param>
        /// <param name="filterByGameMode"></param>
        /// <param name="count"></param>
        /// <param name="currentQueueId"></param>
        /// <returns></returns>
        public async Task<JArray> GetPlayerMatchesAsync(
        string puuid,
        bool filterByGameMode,
        int count = 20,
        string currentQueueId = null)
        {
            // 兜底，防止异常值
            if (count != 10 && count != 20 && count != 30 && count != 50)
                count = 20;

            string queueId = currentQueueId ?? await GetCurrentQueueIdAsync();
            if (!filterByGameMode)
            {
                return await Globals.sgpClient.SgpFetchLatestMatches(puuid, 0, count, "");
            }

            string queueFilter = GetQueueFilter(queueId);
            return await Globals.sgpClient.SgpFetchLatestMatches(puuid, 0, count, queueFilter);
        }

        private async Task<string> GetCurrentQueueIdAsync()
        {
            try
            {
                var session = await Globals.lcuClient.GetChampSelectSession();
                if (session != null)
                {
                    return session["queueId"]?.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[模式过滤] 获取 session 失败: {ex.Message}");
            }
            return Globals.CurrGameMod;
        }

        private string GetQueueFilter(string queueId)
        {
            return queueId switch
            {
                // 排位
                "420" => "q_420",
                "440" => "q_440",

                // 匹配
                "400" => "q_400",
                "430" => "q_430",

                // 大乱斗 & 无限
                "450" => "q_450",
                "900" => "q_900",
                "1900" => "q_1900",

                // 快速模式
                "480" => "q_480",

                // 斗魂 / 海克斯
                "1700" => "q_1700",
                "2400" => "q_2400",
                "2450" => "q_2450",

                // 其他特殊
                "1020" => "q_1020",
                "1300" => "q_1300",
                "1400" => "q_1400",
                "700" => "q_700",

                // 人机（完整）
                "830" => "q_830",
                "840" => "q_840",
                "850" => "q_850",
                "870" => "q_870",
                "880" => "q_880",
                "890" => "q_890",

                // 新手教程
                "2000" => "q_2000",
                "2010" => "q_2010",
                "2020" => "q_2020",

                // 云顶
                "1090" => "q_1090",
                "1100" => "q_1100",
                "1130" => "q_1130",
                "1160" => "q_1160",

                // 自定义 / 训练 / 经典
                "0" => "",               // 自定义一般不过滤
                "3100" => "q_3100",
                "3140" => "q_3140",
                "3270" => "q_3270",
                "4310" => "q_4310",
                "4320" => "q_4320",

                // 末日人机
                "950" => "q_950",
                "960" => "q_960",

                // 旧的云顶兼容（如果还需要）
                "720" => "q_720",
                "740" => "q_740",
                "750" => "q_750",

                _ => ""
            };
        }
    }
}