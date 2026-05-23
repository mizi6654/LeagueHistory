using League.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace League.Parsers
{
    public class PlayerMatchCacheManager
    {
        private readonly Dictionary<long, PlayerMatchInfo> _playerMatchCache = new();

        public bool TryGetPlayerMatch(long summonerId, out PlayerMatchInfo info)
        {
            return _playerMatchCache.TryGetValue(summonerId, out info);
        }

        public void CachePlayerMatch(long summonerId, PlayerMatchInfo info)
        {
            if (summonerId != 0)
                _playerMatchCache[summonerId] = info;
        }

        public void ClearCache()
        {
            _playerMatchCache.Clear();
            Debug.WriteLine("[缓存清理] PlayerMatchCache 已清空");
        }
    }
}