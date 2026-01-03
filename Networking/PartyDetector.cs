using League.Models;

namespace League.Networking 
{
    public class PartyDetector
    {
        private readonly int _matchThreshold;
        private readonly Color[] _colorPool =
        {
        Color.FromArgb(255, 0, 0),       // 红
        Color.FromArgb(0, 0, 255),       // 深蓝
    };

        private Dictionary<string, string> parentMap = new Dictionary<string, string>();
        private int colorIndex = 0;

        public PartyDetector(int matchThreshold = 2)
        {
            _matchThreshold = matchThreshold;
        }

        public void Detect(List<PlayerMatchInfo> players)
        {
            parentMap.Clear();
            colorIndex = 0;

            // 初始化并查集
            foreach (var p in players)
                parentMap[p.Player.Puuid] = p.Player.Puuid;

            // 合并有相同比赛的玩家
            for (int i = 0; i < players.Count; i++)
            {
                var a = players[i];
                for (int j = i + 1; j < players.Count; j++)
                {
                    var b = players[j];
                    int shared = a.Matches.Intersect(b.Matches).Count();
                    if (shared >= _matchThreshold)
                    {
                        Union(a.Player.Puuid, b.Player.Puuid);
                    }
                }
            }

            // 根据根节点分组
            var groupMap = new Dictionary<string, List<PlayerMatchInfo>>();
            foreach (var p in players)
            {
                string root = Find(p.Player.Puuid);
                if (!groupMap.ContainsKey(root))
                    groupMap[root] = new List<PlayerMatchInfo>();
                groupMap[root].Add(p);
            }

            // 分配颜色和GroupKey
            foreach (var group in groupMap.Values)
            {
                if (group.Count >= 2)
                {
                    var key = string.Join("_", group.Select(p => p.Player.GameName).OrderBy(n => n));
                    var color = _colorPool[colorIndex % _colorPool.Length];
                    colorIndex++;

                    foreach (var p in group)
                    {
                        p.Player.GroupKey = key;
                        p.PartyId = key; // 新增：赋值 PartyId，供外部 UI 判断是否变化
                        p.Player.NameColor = color;
                    }
                }
                else
                {
                    var p = group[0];
                    p.Player.GroupKey = null;
                    p.PartyId = null; // 非组队成员，PartyId 置空
                    p.Player.NameColor = Color.Black;
                }
            }
        }

        private string Find(string puuid)
        {
            if (parentMap[puuid] != puuid)
                parentMap[puuid] = Find(parentMap[puuid]);
            return parentMap[puuid];
        }

        private void Union(string puuidA, string puuidB)
        {
            string rootA = Find(puuidA);
            string rootB = Find(puuidB);
            if (rootA != rootB)
                parentMap[rootB] = rootA;
        }
    }

}