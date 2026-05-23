using League.Managers;
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

        public async Task<JArray> GetPlayerMatchesAsync(string puuid, bool filterByGameMode, string currentQueueId = null)
        {
            string queueId = currentQueueId ?? await GetCurrentQueueIdAsync();
            if (!filterByGameMode)
            {
                return await Globals.sgpClient.SgpFetchLatestMatches(puuid, 0, 20, "");
            }

            string queueFilter = GetQueueFilter(queueId);
            return await Globals.sgpClient.SgpFetchLatestMatches(puuid, 0, 20, queueFilter);
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
                "420" => "q_420",
                "440" => "q_440",
                "400" => "q_400",
                "430" => "q_430",
                "450" => "q_450",
                "480" => "q_480",
                "890" => "q_890",
                "900" => "q_900",
                "1020" => "q_1020",
                "1300" => "q_1300",
                "1400" => "q_1400",
                "1700" => "q_1700",
                "2400" => "q_2400",
                "3270" => "q_3270",
                "950" => "q_950",
                "960" => "q_960",
                "1900" => "q_1900",
                "2000" => "q_2000",
                "2010" => "q_2010",
                "2020" => "q_2020",
                "700" => "q_700",
                "720" => "q_720",
                "740" => "q_740",
                "750" => "q_750",
                "1090" => "q_1090",
                "1100" => "q_1100",
                _ => ""
            };
        }
    }
}