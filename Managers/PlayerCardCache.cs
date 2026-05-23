using League.Models;
using League.uitls;
using System.Collections.Concurrent;

namespace League.Managers
{
    public class PlayerCardCache
    {
        private readonly Dictionary<long, PlayerMatchInfo> _cachedPlayerMatchInfos = new();
        private readonly Dictionary<long, int> _currentChampBySummoner = new();
        private readonly Dictionary<long, int> _summonerToColMap = new();
        private readonly Dictionary<long, PlayerMatchInfo> playerMatchCache = new();
        private readonly Dictionary<(int row, int column), (long summonerId, int championId)> playerCache = new();
        private readonly ConcurrentDictionary<long, PlayerCardControl> _cardBySummonerId = new();

        public void AddOrUpdateCache(long summonerId, PlayerMatchInfo info)
        {
            if (summonerId <= 0) return;
            lock (_cachedPlayerMatchInfos)
            {
                _cachedPlayerMatchInfos[summonerId] = info;
            }
        }

        public Dictionary<long, PlayerMatchInfo> GetAllCachedPlayerInfos()
        {
            lock (_cachedPlayerMatchInfos)
            {
                return new Dictionary<long, PlayerMatchInfo>(_cachedPlayerMatchInfos);
            }
        }

        public bool TryGetCard(long summonerId, out PlayerCardControl card)
        {
            return _cardBySummonerId.TryGetValue(summonerId, out card);
        }

        public void RegisterCard(long summonerId, PlayerCardControl card)
        {
            if (summonerId > 0)
                _cardBySummonerId[summonerId] = card;
        }

        //public void UpdateCurrentChampion(long summonerId, int championId, int column)
        //{
        //    if (summonerId > 0)
        //    {
        //        _currentChampBySummoner[summonerId] = championId;
        //        _summonerToColMap[summonerId] = column;
        //    }
        //}
        public void UpdateCurrentChampion(long summonerId, int championId, int column)
        {
            if (summonerId > 0 || championId > 0)  // 隐藏玩家也允许更新 championId
            {
                _currentChampBySummoner[summonerId] = championId;
                _summonerToColMap[summonerId] = column;
            }
        }

        public int GetCurrentChampion(long summonerId) =>
            _currentChampBySummoner.TryGetValue(summonerId, out var champ) ? champ : 0;

        public void ClearAll()
        {
            playerMatchCache.Clear();
            playerCache.Clear();
            _cachedPlayerMatchInfos.Clear();
            _currentChampBySummoner.Clear();
            _summonerToColMap.Clear();
            _cardBySummonerId.Clear();
        }

        public void ClearGameState()
        {
            _currentChampBySummoner.Clear();
            _summonerToColMap.Clear();
            _cachedPlayerMatchInfos.Clear();
            playerMatchCache.Clear();
            _cardBySummonerId.Clear();
        }
    }
}