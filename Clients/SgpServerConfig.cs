using System.Diagnostics;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace League.Clients
{
    public static class SgpServerConfig
    {
        private static Dictionary<string, (string MatchHistory, string Common)> _servers;
        private static bool _loaded;

        public static void Load(string explicitPath = null)
        {
            if (_loaded) return;
            try
            {
                string json = null;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var paths = new List<string>();
                if (!string.IsNullOrEmpty(explicitPath)) paths.Add(explicitPath);
                paths.Add(Path.Combine(baseDir, "Config", "sgp-servers.json"));
                paths.Add(Path.Combine(baseDir, "sgp-servers.json"));

                foreach (var p in paths)
                {
                    if (File.Exists(p))
                    {
                        json = File.ReadAllText(p);
                        Debug.WriteLine($"[SGP Config] 加载: {p}");
                        break;
                    }
                }

                if (json == null)
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var name = asm.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith("sgp-servers.json", StringComparison.OrdinalIgnoreCase));
                    if (name != null)
                    {
                        using var s = asm.GetManifestResourceStream(name);
                        using var r = new StreamReader(s);
                        json = r.ReadToEnd();
                    }
                }

                _servers = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(json))
                {
                    var root = JObject.Parse(json);
                    var servers = root["servers"] as JObject;
                    if (servers != null)
                    {
                        foreach (var prop in servers.Properties())
                        {
                            var mh = prop.Value["matchHistory"]?.ToString()?.TrimEnd('/');
                            var common = prop.Value["common"]?.ToString()?.TrimEnd('/');
                            if (!string.IsNullOrEmpty(mh))
                                _servers[prop.Name] = (mh, common ?? mh);
                        }
                    }
                    Debug.WriteLine($"[SGP Config] {_servers.Count} 个服务器");
                }
                else
                {
                    Debug.WriteLine("[SGP Config] 未找到 sgp-servers.json");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SGP Config] {ex.Message}");
                _servers ??= new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            }
            _loaded = true;
        }

        public static bool TryGetMatchHistory(string sgpServerId, out string url)
        {
            url = null;
            if (!_loaded) Load();
            if (string.IsNullOrEmpty(sgpServerId) || _servers == null) return false;
            if (_servers.TryGetValue(sgpServerId, out var ep))
            {
                url = ep.MatchHistory;
                return !string.IsNullOrEmpty(url);
            }
            return false;
        }

        /// <summary>
        /// 把 LCU 的 region / rsoPlatformId 转成 JSON 里的 key。
        /// 不枚举列表；JSON 有什么 key 就支持什么服。
        /// </summary>
        public static string BuildSgpServerId(string region, string rsoPlatformId)
        {
            if (string.IsNullOrWhiteSpace(rsoPlatformId) && string.IsNullOrWhiteSpace(region))
                return null;

            string regionTrim = region?.Trim();
            string rsoTrim = rsoPlatformId?.Trim();

            // 外服：优先用 rso/platformId（TW2、SG2、JP1…）
            if (!string.IsNullOrWhiteSpace(rsoTrim))
            {
                // 已是 TENCENT_HN1 这种
                if (rsoTrim.StartsWith("TENCENT_", StringComparison.OrdinalIgnoreCase))
                    return rsoTrim.ToUpperInvariant();

                // 国服子区：HN1 / NJ100 / BGP2…
                bool rsoLooksTencent =
                    rsoTrim.StartsWith("HN", StringComparison.OrdinalIgnoreCase) ||
                    rsoTrim.StartsWith("NJ", StringComparison.OrdinalIgnoreCase) ||
                    rsoTrim.StartsWith("GZ", StringComparison.OrdinalIgnoreCase) ||
                    rsoTrim.StartsWith("CQ", StringComparison.OrdinalIgnoreCase) ||
                    rsoTrim.StartsWith("TJ", StringComparison.OrdinalIgnoreCase) ||
                    rsoTrim.StartsWith("BGP", StringComparison.OrdinalIgnoreCase);

                if (rsoLooksTencent ||
                    string.Equals(regionTrim, "TENCENT", StringComparison.OrdinalIgnoreCase))
                {
                    // 只有真正的子区才加前缀；单独的 "TENCENT" 不能当子区
                    if (string.Equals(rsoTrim, "TENCENT", StringComparison.OrdinalIgnoreCase))
                        return null;
                    return rsoLooksTencent
                        ? "TENCENT_" + rsoTrim.ToUpperInvariant()
                        : rsoTrim.ToUpperInvariant();
                }

                // 纯外服 platformId
                return rsoTrim.ToUpperInvariant();
            }

            // 只有 region、没有 rso
            // region=TENCENT 无法对应具体大区，返回 null，让后面走 LCU 补全 / 动态解析
            if (string.Equals(regionTrim, "TENCENT", StringComparison.OrdinalIgnoreCase))
                return null;

            // 外服有时只有 region=TW 等，未必够用，仍返回供尝试；表 miss 再补全
            return regionTrim.ToUpperInvariant();
        }
    }
}