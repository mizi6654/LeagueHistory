using Newtonsoft.Json.Linq;

namespace League.Models
{
    public class MatchInfo : IDisposable
    {
        private bool _disposed;

        private string _resultText;

        public string MatchId { get; set; }

        public string ResultText
        {
            get
            {
                return _resultText;
            }
            set
            {
                _resultText = value;
                ResultColor = value == "胜利" ? Color.Green : Color.Red;
            }
        }

        public Color ResultColor { get; set; } = Color.Red;

        public int Index { get; set; } = 0;

        public string CurrPuuid { get; set; } = "";

        public string CurrSummonerId { get; set; } = "";

        public long GameId { get; set; } = 0L;

        public string DurationText { get; set; } = "";

        public string Mode { get; set; } = "";

        public string QueueId { get; set; } = "";

        public string GameTime { get; set; } = "";

        public Image HeroIcon { get; set; } = new Bitmap(1, 1);

        public int Kills { get; set; }

        public int Deaths { get; set; }

        public int Assists { get; set; }

        public Image[] SummonerSpells { get; set; } = new Image[2];

        public Item[] Items { get; set; } = (from _ in Enumerable.Range(0, 6)
                                             select new Item()).ToArray();

        public string GoldText { get; set; } = "";

        public string DamageText { get; set; } = "";

        public string DamageTaken { get; set; } = "";

        public string KPPercentage { get; set; } = "";

        public List<PlayerInfo> BlueTeam { get; set; } = new List<PlayerInfo>();

        public List<PlayerInfo> RedTeam { get; set; } = new List<PlayerInfo>();

        public RuneInfo[] PrimaryRunes { get; set; } = (from _ in Enumerable.Range(0, 4)
                                                        select new RuneInfo()).ToArray();

        public RuneInfo[] SecondaryRunes { get; set; } = (from _ in Enumerable.Range(0, 2)
                                                          select new RuneInfo()).ToArray();

        public PlayerInfo SelfPlayer { get; set; } = new PlayerInfo();

        public string ChampionName { get; set; } = "";

        public string ChampionDescription { get; set; } = "";

        public string[] SpellNames { get; set; } = new string[2];

        public string[] SpellDescriptions { get; set; } = new string[2];

        public string[] ItemNames { get; set; } = new string[6];

        public string[] ItemDescriptions { get; set; } = new string[6];

        public string[] PrimaryRuneNames { get; set; } = new string[4];

        public string[] PrimaryRuneDescriptions { get; set; } = new string[4];

        public string[] SecondaryRuneNames { get; set; } = new string[2];

        public string[] SecondaryRuneDescriptions { get; set; } = new string[2];

        public string DamageTakenText { get; set; } = "";

        public RuneInfo[] StatRunes { get; set; } = (from _ in Enumerable.Range(0, 3)
                                                     select new RuneInfo()).ToArray();

        public string[] StatRuneNames { get; set; } = new string[3];

        public string[] StatRuneDescriptions { get; set; } = new string[3];

        public JObject RawGameData { get; set; }

        public List<JObject> AllParticipants { get; set; }

        public bool IsReplayDownloaded { get; set; } = false;

        public MatchInfo()
        {
            PrimaryRunes = (from _ in Enumerable.Range(0, 4)
                            select new RuneInfo()).ToArray();
            SecondaryRunes = (from _ in Enumerable.Range(0, 2)
                              select new RuneInfo()).ToArray();
            Items = (from _ in Enumerable.Range(0, 6)
                     select new Item()).ToArray();
            SpellNames = new string[2];
            SpellDescriptions = new string[2];
            ItemNames = new string[6];
            ItemDescriptions = new string[6];
            PrimaryRuneNames = new string[4];
            PrimaryRuneDescriptions = new string[4];
            SecondaryRuneNames = new string[2];
            SecondaryRuneDescriptions = new string[2];

            // 初始化 Augments 数组
            Augments = Array.Empty<RuneInfo>();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            HeroIcon?.Dispose();
            if (SummonerSpells != null)
            {
                Image[] summonerSpells = SummonerSpells;
                for (int i = 0; i < summonerSpells.Length; i++)
                {
                    summonerSpells[i]?.Dispose();
                }
            }
            if (Items != null)
            {
                Item[] items = Items;
                for (int j = 0; j < items.Length; j++)
                {
                    items[j]?.Icon?.Dispose();
                }
            }
            if (PrimaryRunes != null)
            {
                RuneInfo[] primaryRunes = PrimaryRunes;
                for (int k = 0; k < primaryRunes.Length; k++)
                {
                    primaryRunes[k]?.Icon?.Dispose();
                }
            }
            if (SecondaryRunes != null)
            {
                RuneInfo[] secondaryRunes = SecondaryRunes;
                for (int l = 0; l < secondaryRunes.Length; l++)
                {
                    secondaryRunes[l]?.Icon?.Dispose();
                }
            }
            if (StatRunes != null)
            {
                RuneInfo[] statRunes = StatRunes;
                for (int m = 0; m < statRunes.Length; m++)
                {
                    statRunes[m]?.Icon?.Dispose();
                }
            }
            if (BlueTeam != null)
            {
                foreach (PlayerInfo item2 in BlueTeam)
                {
                    item2?.Avatar?.Dispose();
                }
            }
            if (RedTeam != null)
            {
                foreach (PlayerInfo item3 in RedTeam)
                {
                    item3?.Avatar?.Dispose();
                }
            }
            // 释放所有玩家的海克斯符文
            if (AllPlayersAugments != null)
            {
                foreach (var augments in AllPlayersAugments.Values)
                {
                    if (augments != null)
                    {
                        foreach (var augment in augments)
                        {
                            augment?.Icon?.Dispose();
                        }
                    }
                }
                AllPlayersAugments.Clear();
            }
            SelfPlayer?.Avatar?.Dispose();
            RawGameData = null;
            AllParticipants = null;
            GC.SuppressFinalize(this);
        }

        // 海克斯大乱斗符文
        public RuneInfo[] Augments { get; set; } = Array.Empty<RuneInfo>();

        // 新增：所有玩家的海克斯符文，按puuid索引
        public Dictionary<string, RuneInfo[]> AllPlayersAugments { get; set; } = new Dictionary<string, RuneInfo[]>();
    }
}
