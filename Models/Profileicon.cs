using System.Collections.Concurrent;
using System.Diagnostics;

namespace League.Models
{
    public class Profileicon
    {
        private static readonly Image DefaultProfile = Image.FromFile("Assets/Defaults/Profile.png");
        private static readonly HttpClient _httpClient = CreateHttpClient();
        private static readonly SemaphoreSlim _fileLock = new(1, 1);
        private static readonly ConcurrentDictionary<string, Image> _imageCache = new();
        private static string VersionCachePath => Path.Combine("Assets", "ddragon_version.txt");
        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueTools/1.0");
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
        }

        #region 读取本地版本号

        /// <summary>
        /// 获取本地版本号
        /// </summary>
        /// <returns></returns>
        private static async Task<string> GetCachedDdragonVersionAsync()
        {
            try
            {
                if (File.Exists(VersionCachePath))
                {
                    var version = await File.ReadAllTextAsync(VersionCachePath);
                    return version?.Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[读取本地版本失败] {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 获取国际服最新的版本号
        /// </summary>
        /// <param name="forceUpdate"></param>
        /// <returns></returns>
        private static async Task<string> GetLatestDdragonVersionAsync(bool forceUpdate = false)
        {
            try
            {
                using var client = new HttpClient();
                var json = await client.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json");
                var versions = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                var latest = versions?.FirstOrDefault() ?? "15.12.1";

                Directory.CreateDirectory(Path.GetDirectoryName(VersionCachePath));
                await File.WriteAllTextAsync(VersionCachePath, latest);

                Debug.WriteLine($"[写入最新版本] {latest}");
                return latest;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[获取远程版本失败] {ex.Message}");
                return "15.12.1";
            }
        }

        private static async Task<string> GetOrLoadDdragonVersionAsync()
        {
            var cached = await GetCachedDdragonVersionAsync();
            if (!string.IsNullOrWhiteSpace(cached))
            {
                Debug.WriteLine($"[版本缓存命中] {cached}");
                return cached;
            }

            return await GetLatestDdragonVersionAsync(forceUpdate: true);
        }
        #endregion

        public static async Task<Image> GetProfileIconAsync(int profileIconId)
        {
            return await GetImageAsync("profileicon", profileIconId);
        }

        /// <summary>
        /// 根据玩家头像ID下载玩家头像
        /// </summary>
        /// <param name="type"></param>
        /// <param name="profileIconId"></param>
        /// <returns></returns>
        private static async Task<Image> GetImageAsync(string type, int profileIconId)
        {
            if (profileIconId <= 0)
            {
                Debug.WriteLine($"[图片加载] 无效 ID: {type}/{profileIconId}");
                return GetDefaultImage(type);
            }

            var cacheKey = $"{type}/{profileIconId}";
            if (_imageCache.TryGetValue(cacheKey, out var cachedImage))
                return cachedImage;

            var localPath = Path.Combine("Assets", type, $"{profileIconId}.png");
            if (File.Exists(localPath))
            {
                var image = LoadImageSafe(localPath);
                if (image != null)
                {
                    _imageCache.TryAdd(cacheKey, image);
                    return image;
                }
            }

            // Step 1: 国服地址优先尝试
            var cnUrl = $"https://game.gtimg.cn/images/lol/act/img/profileicon/{profileIconId}.png";
            try
            {
                await _fileLock.WaitAsync();
                Debug.WriteLine($"[尝试国服下载] {cnUrl}");
                var cnBytes = await _httpClient.GetByteArrayAsync(cnUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                await File.WriteAllBytesAsync(localPath, cnBytes);

                var image = LoadImageSafe(localPath);
                if (image != null)
                {
                    _imageCache.TryAdd(cacheKey, image);
                    return image;
                }
            }
            catch
            {
                Debug.WriteLine($"[国服下载失败] {cnUrl}");
            }
            finally
            {
                _fileLock.Release();
            }

            // 优先使用本地版本，如果没有才去远程
            string version = await GetOrLoadDdragonVersionAsync();

            var intlUrl = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/profileicon/{profileIconId}.png";
            try
            {
                await _fileLock.WaitAsync();
                Debug.WriteLine($"[尝试国际服下载] {intlUrl}");
                var intlBytes = await _httpClient.GetByteArrayAsync(intlUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                await File.WriteAllBytesAsync(localPath, intlBytes);

                var image = LoadImageSafe(localPath);
                if (image != null)
                {
                    _imageCache.TryAdd(cacheKey, image);
                    return image;
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[国际服下载失败: 本地版本] → {ex.Message}");

                // 重新强制获取远程版本 + 保存
                version = await GetLatestDdragonVersionAsync(forceUpdate: true);
                intlUrl = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/profileicon/{profileIconId}.png";
                try
                {
                    var retryBytes = await _httpClient.GetByteArrayAsync(intlUrl);
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                    await File.WriteAllBytesAsync(localPath, retryBytes);

                    var image = LoadImageSafe(localPath);
                    if (image != null)
                    {
                        _imageCache.TryAdd(cacheKey, image);
                        return image;
                    }
                }
                catch (HttpRequestException retryEx)
                {
                    Debug.WriteLine($"[国际服下载失败: 最新版本] → {retryEx.Message}");
                }
            }
            finally
            {
                _fileLock.Release();
            }

            return GetDefaultImage(type);
        }


        /// <summary>
        /// 安全读取本地图片，确保文件读取流释放，避免加载异常
        /// </summary>
        private static Image LoadImageSafe(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Seek(0, SeekOrigin.Begin);
                return Image.FromStream(ms);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[本地图片读取失败] {path} → {ex.Message}");
                return null;
            }
        }

        private static Image GetDefaultImage(string type) => type switch
        {
            "profileicon" => DefaultProfile,
            _ => null
        };
    }
}
