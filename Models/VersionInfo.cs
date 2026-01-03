using Newtonsoft.Json;

namespace League.Models
{
    public class VersionInfo
    {
        public string version { get; set; }
        public string date { get; set; }
        public List<string> changelog { get; set; }
        public string updateUrl { get; set; }

        public static VersionInfo GetLocalVersion()
        {
            try
            {
                var versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
                if (!File.Exists(versionFile))
                    return null;

                return new VersionInfo
                {
                    version = File.ReadAllText(versionFile).Trim()
                };
            }
            catch
            {
                return null;
            }
        }

        public static async Task<VersionInfo> GetRemoteVersion()
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                    var response = await httpClient.GetAsync("https://gitee.com/annals-code/league-update/raw/master/version.json");

                    if (!response.IsSuccessStatusCode)
                        return null;

                    var content = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<VersionInfo>(content);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}