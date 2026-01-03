using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Caching
{
    public static class MatchCache
    {
        private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _cache = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

        private static string MakePageKey(string tag, int begIndex, int pageSize)
        {
            return $"{tag ?? "all"}_{begIndex}_{pageSize}";
        }

        public static void Add(string puuid, string tag, int begIndex, int pageSize, JArray games)
        {
            if (games != null && games.Count != 0)
            {
                string json = games.ToString(Formatting.None);
                if (!_cache.ContainsKey(puuid))
                {
                    _cache[puuid] = new Dictionary<string, Dictionary<string, string>>();
                }
                string tagKey = tag ?? "all";
                if (!_cache[puuid].ContainsKey(tagKey))
                {
                    _cache[puuid][tagKey] = new Dictionary<string, string>();
                }
                string pageKey = MakePageKey(tag, begIndex, pageSize);
                _cache[puuid][tagKey][pageKey] = json;
                if (_cache[puuid][tagKey].Count > 5)
                {
                    string firstKey = new List<string>(_cache[puuid][tagKey].Keys)[0];
                    _cache[puuid][tagKey].Remove(firstKey);
                }
            }
        }

        public static JArray Get(string puuid, string tag, int begIndex, int pageSize)
        {
            string pageKey = MakePageKey(tag, begIndex, pageSize);
            if (_cache.TryGetValue(puuid, out Dictionary<string, Dictionary<string, string>> modeCache) && modeCache.TryGetValue(tag ?? "all", out var pageCache) && pageCache.TryGetValue(pageKey, out var json))
            {
                return JArray.Parse(json);
            }
            return null;
        }

        public static void ClearPlayer(string puuid)
        {
            _cache.Remove(puuid);
        }

        public static void ClearAll()
        {
            _cache.Clear();
            GC.Collect();
        }
    }
}
