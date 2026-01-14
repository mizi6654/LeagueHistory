using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Models
{
    public class VersionInfo
    {
        public string version { get; set; }
        public string date { get; set; }
        public List<string> changelog { get; set; }
        public string updateUrl { get; set; }

        
        /// <summary>
        /// 获取本地版本（version.txt 内容，如 1.0.5）
        /// </summary>
        public static string GetLocalVersion()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
                return File.Exists(path) ? File.ReadAllText(path).Trim() : "0.0.0";
            }
            catch { return "0.0.0"; }
        }

        /// <summary>
        /// 把 v1.0.6、1.0.6、V1.0.6 统一转成 Version 对象方便比较
        /// </summary>
        public static Version Parse(string ver)
        {
            ver = ver.Trim().TrimStart('v', 'V');
            return Version.TryParse(ver, out var v) ? v : new Version(0, 0);
        }

        public static async Task<VersionInfo> GetRemoteVersion()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueHistoryUpdater/1.0");  // GitHub 强制要求

                var url = "https://api.github.com/repos/mizi6654/LeagueHistory/releases/latest";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var jsonStr = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(jsonStr);

                string tag = json["tag_name"]?.ToString()?.TrimStart('v') ?? "";
                if (string.IsNullOrEmpty(tag)) return null;

                var assets = json["assets"] as JArray;
                var zip = assets?
                    .FirstOrDefault(a => a["name"]?.ToString().Contains(".zip", StringComparison.OrdinalIgnoreCase) == true);

                return new VersionInfo
                {
                    version = tag,
                    date = DateTime.TryParse(json["published_at"]?.ToString(), out var dt) ? dt.ToString("yyyy-MM-dd") : "",
                    changelog = (json["body"]?.ToString() ?? "详见 GitHub 更新日志")
                                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                    .ToList(),
                    updateUrl = zip?["browser_download_url"]?.ToString() ?? json["html_url"]?.ToString()
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return null;
            }
        }
    }
}