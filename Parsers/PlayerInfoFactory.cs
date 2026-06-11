using League.Managers;
using League.Models;
using static League.FormMain;

namespace League.Parsers
{
    public class PlayerInfoFactory
    {
        private readonly PlayerCardManager _playerCardManager;

        public PlayerInfoFactory(PlayerCardManager playerCardManager)
        {
            _playerCardManager = playerCardManager;
        }

        public PlayerMatchInfo CreateHiddenPlayerInfo(long summonerId, int championId)
        {
            string championName = Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown";
            Image championIcon = Task.Run(() => Globals.resLoading.GetChampionIconAsync(championId)).Result
                               ?? LoadDefaultImage();

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
            return _playerCardManager.CreateFailedPlayerInfo(summonerId, championId);
        }

        private Image LoadDefaultImage()
        {
            try
            {
                var path = AppDomain.CurrentDomain.BaseDirectory + "Assets\\Defaults\\Profile.png";
                if (File.Exists(path)) return Image.FromFile(path);
            }
            catch { }
            return new Bitmap(64, 64);
        }
    }
}