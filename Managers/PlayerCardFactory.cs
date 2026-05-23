using League.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Managers
{
    public class PlayerCardFactory
    {
        public PlayerMatchInfo CreateLoadingPlayerInfo(JToken p)
        {
            long summonerId = p["summonerId"]?.Value<long>() ?? 0;
            int championId = p["championId"]?.Value<int>() ?? 0;

            string championName = Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown";

            // 【关键修复】提前加载头像（隐藏玩家也需要）
            Image avatar = Task.Run(async () =>
                await Globals.resLoading.GetChampionIconAsync(championId)).Result;

            var playerInfo = new PlayerInfo
            {
                SummonerId = summonerId,
                ChampionId = championId,
                ChampionName = championName,
                GameName = summonerId == 0 ? "隐藏玩家" : "加载中...",
                SoloRank = summonerId == 0 ? "隐藏" : "加载中...",
                FlexRank = summonerId == 0 ? "隐藏" : "加载中...",
                IsPublic = summonerId == 0 ? "隐藏" : "[查询中]",
                Avatar = avatar,                    
                NameColor = summonerId == 0 ? Color.Gray : Color.White
            };

            return new PlayerMatchInfo
            {
                Player = playerInfo,
                MatchItems = new List<ListViewItem>(),
                HeroIcons = new ImageList()
            };
        }

        public PlayerMatchInfo CreateHiddenPlayerInfo(long summonerId, int championId)
        {
            string championName = Globals.resLoading.GetChampionById(championId)?.Name ?? "未知英雄";
            Image championIcon = Task.Run(() => Globals.resLoading.GetChampionIconAsync(championId)).Result
                                 ?? LoadErrorImage();

            return new PlayerMatchInfo
            {
                Player = new PlayerInfo
                {
                    SummonerId = summonerId,
                    ChampionId = championId,
                    ChampionName = championName,
                    Avatar = championIcon,
                    GameName = "隐藏玩家",
                    SoloRank = "隐藏",
                    FlexRank = "隐藏",
                    IsPublic = "隐藏",
                    NameColor = Color.Gray
                },
                MatchItems = new List<ListViewItem>(),
                HeroIcons = new ImageList()
            };
        }

        public PlayerMatchInfo CreateFailedPlayerInfo(long summonerId, int championId)
        {
            string championName = Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown";
            Image championIcon = Task.Run(() => Globals.resLoading.GetChampionIconAsync(championId)).Result
                                 ?? LoadErrorImage();

            if (summonerId == 0)
                return CreateHiddenPlayerInfo(0, championId);

            return new PlayerMatchInfo
            {
                Player = new PlayerInfo
                {
                    SummonerId = summonerId,
                    ChampionId = championId,
                    ChampionName = "查询失败",
                    GameName = "失败",
                    IsPublic = "[失败]",
                    SoloRank = "失败",
                    FlexRank = "失败",
                    Avatar = championIcon,
                    NameColor = Color.DarkRed
                },
                MatchItems = new List<ListViewItem>(),
                HeroIcons = new ImageList()
            };
        }

        private Image LoadErrorImage()
        {
            try
            {
                return Image.FromFile(AppDomain.CurrentDomain.BaseDirectory + "Assets\\Defaults\\Profile.png");
            }
            catch
            {
                return null;
            }
        }
    }
}