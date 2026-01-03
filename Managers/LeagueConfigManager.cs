using League.Models;
using Newtonsoft.Json;

namespace League.Managers
{
    public class LeagueConfigManager
    {
        private static readonly string ConfigPath = "LeagueConfig.json";

        public static LeagueConfig Load()
        {
            if (!File.Exists(ConfigPath))
                return new LeagueConfig();

            return JsonConvert.DeserializeObject<LeagueConfig>(
                File.ReadAllText(ConfigPath)
            ) ?? new LeagueConfig();
        }

        public static void Save(LeagueConfig config)
        {
            File.WriteAllText(
                ConfigPath,
                JsonConvert.SerializeObject(config, Formatting.Indented)
            );
        }
    }
}
