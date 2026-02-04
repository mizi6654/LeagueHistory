//using System.Collections.Concurrent;
//using System.Diagnostics;

//namespace League.Models
//{
//    public class Profileicon
//    {
//        private static readonly Image DefaultProfile = Image.FromFile("Assets/Defaults/Profile.png");
//        private static readonly HttpClient _httpClient = CreateHttpClient();
//        private static readonly SemaphoreSlim _fileLock = new(1, 1);
//        private static readonly ConcurrentDictionary<string, Image> _imageCache = new();
//        private static string VersionCachePath => Path.Combine("Assets", "ddragon_version.txt");

//        private static HttpClient CreateHttpClient()
//        {
//            var client = new HttpClient();
//            client.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueTools/1.0");
//            client.Timeout = TimeSpan.FromSeconds(15);
//            return client;
//        }

//        #region 读取本地版本号

//        /// <summary>
//        /// 获取本地版本号
//        /// </summary>
//        /// <returns></returns>
//        private static async Task<string> GetCachedDdragonVersionAsync()
//        {
//            try
//            {
//                if (File.Exists(VersionCachePath))
//                {
//                    var version = await File.ReadAllTextAsync(VersionCachePath);
//                    return version?.Trim();
//                }
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[读取本地版本失败] {ex.Message}");
//            }
//            return null;
//        }

//        /// <summary>
//        /// 获取国际服最新的版本号
//        /// </summary>
//        /// <param name="forceUpdate"></param>
//        /// <returns></returns>
//        private static async Task<string> GetLatestDdragonVersionAsync(bool forceUpdate = false)
//        {
//            try
//            {
//                using var client = new HttpClient();
//                var json = await client.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json");
//                var versions = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
//                var latest = versions?.FirstOrDefault() ?? "15.12.1";

//                Directory.CreateDirectory(Path.GetDirectoryName(VersionCachePath));
//                await File.WriteAllTextAsync(VersionCachePath, latest);

//                Debug.WriteLine($"[写入最新版本] {latest}");
//                return latest;
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[获取远程版本失败] {ex.Message}");
//                return "15.12.1";
//            }
//        }

//        private static async Task<string> GetOrLoadDdragonVersionAsync()
//        {
//            var cached = await GetCachedDdragonVersionAsync();
//            if (!string.IsNullOrWhiteSpace(cached))
//            {
//                Debug.WriteLine($"[版本缓存命中] {cached}");
//                return cached;
//            }

//            return await GetLatestDdragonVersionAsync(forceUpdate: true);
//        }
//        #endregion

//        /// <summary>
//        /// 默认头像兜底
//        /// </summary>
//        static Profileicon()
//        {
//            try
//            {
//                DefaultProfile = new Bitmap(Image.FromFile("Assets/Defaults/Profile.png"));
//            }
//            catch
//            {
//                DefaultProfile = new Bitmap(64, 64); // 兜底
//            }
//        }

//        public static async Task<Image> GetProfileIconAsync(int profileIconId)
//        {
//            return await GetImageAsync("profileicon", profileIconId);
//        }

//        /// <summary>
//        /// 根据玩家头像ID下载玩家头像
//        /// </summary>
//        /// <param name="type"></param>
//        /// <param name="profileIconId"></param>
//        /// <returns></returns>
//        private static async Task<Image> GetImageAsync(string type, int profileIconId)
//        {
//            if (profileIconId <= 0)
//            {
//                Debug.WriteLine($"[图片加载] 无效 ID: {type}/{profileIconId}");
//                return GetDefaultImage(type);
//            }

//            var cacheKey = $"{type}/{profileIconId}";
//            if (_imageCache.TryGetValue(cacheKey, out var cachedImage))
//                return cachedImage;

//            var localPath = Path.Combine("Assets", type, $"{profileIconId}.png");
//            if (File.Exists(localPath))
//            {
//                var image = LoadImageSafe(localPath);
//                if (image != null)
//                {
//                    _imageCache.TryAdd(cacheKey, image);
//                    return image;
//                }
//            }

//            // Step 1: 国服地址优先尝试
//            var cnUrl = $"https://game.gtimg.cn/images/lol/act/img/profileicon/{profileIconId}.png";
//            try
//            {
//                await _fileLock.WaitAsync();
//                Debug.WriteLine($"[尝试国服下载] {cnUrl}");
//                var cnBytes = await _httpClient.GetByteArrayAsync(cnUrl);
//                Directory.CreateDirectory(Path.GetDirectoryName(localPath));
//                await File.WriteAllBytesAsync(localPath, cnBytes);

//                var image = LoadImageSafe(localPath);
//                if (image != null)
//                {
//                    _imageCache.TryAdd(cacheKey, image);
//                    return image;
//                }
//            }
//            catch
//            {
//                Debug.WriteLine($"[国服下载失败] {cnUrl}");
//            }
//            finally
//            {
//                _fileLock.Release();
//            }

//            // 优先使用本地版本，如果没有才去远程
//            string version = await GetOrLoadDdragonVersionAsync();

//            var intlUrl = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/profileicon/{profileIconId}.png";
//            try
//            {
//                await _fileLock.WaitAsync();
//                Debug.WriteLine($"[尝试国际服下载] {intlUrl}");
//                var intlBytes = await _httpClient.GetByteArrayAsync(intlUrl);
//                Directory.CreateDirectory(Path.GetDirectoryName(localPath));
//                await File.WriteAllBytesAsync(localPath, intlBytes);

//                var image = LoadImageSafe(localPath);
//                if (image != null)
//                {
//                    _imageCache.TryAdd(cacheKey, image);
//                    return image;
//                }
//            }
//            catch (HttpRequestException ex)
//            {
//                Debug.WriteLine($"[国际服下载失败: 本地版本] → {ex.Message}");

//                // 重新强制获取远程版本 + 保存
//                version = await GetLatestDdragonVersionAsync(forceUpdate: true);
//                intlUrl = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/profileicon/{profileIconId}.png";
//                try
//                {
//                    var retryBytes = await _httpClient.GetByteArrayAsync(intlUrl);
//                    Directory.CreateDirectory(Path.GetDirectoryName(localPath));
//                    await File.WriteAllBytesAsync(localPath, retryBytes);

//                    var image = LoadImageSafe(localPath);
//                    if (image != null)
//                    {
//                        _imageCache.TryAdd(cacheKey, image);
//                        return image;
//                    }
//                }
//                catch (HttpRequestException retryEx)
//                {
//                    Debug.WriteLine($"[国际服下载失败: 最新版本] → {retryEx.Message}");
//                }
//            }
//            finally
//            {
//                _fileLock.Release();
//            }

//            return GetDefaultImage(type);
//        }


//        ///// <summary>
//        ///// 安全读取本地图片，确保文件读取流释放，避免加载异常
//        ///// </summary>
//        //private static Image LoadImageSafe(string path)
//        //{
//        //    try
//        //    {
//        //        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
//        //        using var ms = new MemoryStream();
//        //        fs.CopyTo(ms);
//        //        ms.Seek(0, SeekOrigin.Begin);
//        //        return Image.FromStream(ms);
//        //    }
//        //    catch (Exception ex)
//        //    {
//        //        Debug.WriteLine($"[本地图片读取失败] {path} → {ex.Message}");
//        //        return null;
//        //    }
//        //}

//        /// <summary>
//        /// 安全读取本地图片并返回独立副本
//        /// </summary>
//        private static Image? LoadImageSafe(string path)
//        {
//            if (!File.Exists(path))
//                return null;

//            try
//            {
//                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
//                using var ms = new MemoryStream();
//                fs.CopyTo(ms);
//                ms.Seek(0, SeekOrigin.Begin);

//                // 关键两步：先 FromStream，再 new Bitmap 创建独立副本
//                using var tempImage = Image.FromStream(ms);
//                return new Bitmap(tempImage);        // ← 必须这样写
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[本地图片读取失败] {path} → {ex.Message}");
//                return null;
//            }
//        }

//        private static Image GetDefaultImage(string type) => type switch
//        {
//            "profileicon" => DefaultProfile,
//            _ => null
//        };
//    }
//}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace League.Models
{
    public class Profileicon
    {
        private static readonly HttpClient _httpClient = CreateHttpClient();
        private static readonly SemaphoreSlim _fileLock = new(1, 1);
        private static readonly ConcurrentDictionary<string, Image> _imageCache = new();

        private static readonly Image _defaultProfile;
        private static string VersionCachePath => Path.Combine("Assets", "ddragon_version.txt");

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueTools/1.0");
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
        }

        // 静态构造函数 - 安全加载默认头像
        static Profileicon()
        {
            try
            {
                using var temp = Image.FromFile("Assets/Defaults/Profile.png");
                _defaultProfile = new Bitmap(temp); // 创建独立副本
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[默认头像加载失败] {ex.Message}");
                _defaultProfile = new Bitmap(64, 64); // 纯兜底
            }
        }

        public static async Task<Image> GetProfileIconAsync(int profileIconId)
        {
            return await GetImageAsync("profileicon", profileIconId);
        }

        private static async Task<Image> GetImageAsync(string type, int profileIconId)
        {
            if (profileIconId <= 0)
            {
                Debug.WriteLine($"[图片加载] 无效 ID: {type}/{profileIconId}");
                return GetDefaultImageSafe();
            }

            var cacheKey = $"{type}/{profileIconId}";

            // 缓存命中时返回副本（防止外部 Dispose 影响缓存）
            if (_imageCache.TryGetValue(cacheKey, out var cachedImage) && cachedImage != null)
            {
                return new Bitmap(cachedImage);
            }

            var localPath = Path.Combine("Assets", type, $"{profileIconId}.png");

            // 本地已有文件
            if (File.Exists(localPath))
            {
                var image = LoadImageSafe(localPath);
                if (image != null)
                {
                    _imageCache.TryAdd(cacheKey, image);
                    return new Bitmap(image);   // 返回副本
                }
            }

            // ==================== 下载逻辑 ====================

            // Step 1: 优先尝试国服
            var cnUrl = $"https://game.gtimg.cn/images/lol/act/img/profileicon/{profileIconId}.png";
            try
            {
                await _fileLock.WaitAsync();
                Debug.WriteLine($"[尝试国服下载] {cnUrl}");

                var cnBytes = await _httpClient.GetByteArrayAsync(cnUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await File.WriteAllBytesAsync(localPath, cnBytes);

                var image = LoadImageSafe(localPath);
                if (image != null)
                {
                    _imageCache.TryAdd(cacheKey, image);
                    return new Bitmap(image);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[国服下载失败] {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }

            // Step 2: 国际服（带版本回退）
            string version = await GetOrLoadDdragonVersionAsync();
            var intlUrl = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/profileicon/{profileIconId}.png";

            try
            {
                await _fileLock.WaitAsync();
                Debug.WriteLine($"[尝试国际服下载] {intlUrl}");

                var intlBytes = await _httpClient.GetByteArrayAsync(intlUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await File.WriteAllBytesAsync(localPath, intlBytes);

                var image = LoadImageSafe(localPath);
                if (image != null)
                {
                    _imageCache.TryAdd(cacheKey, image);
                    return new Bitmap(image);
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[国际服下载失败] {ex.Message}");

                // 版本过期时强制刷新版本重试
                version = await GetLatestDdragonVersionAsync(forceUpdate: true);
                intlUrl = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/profileicon/{profileIconId}.png";

                try
                {
                    var retryBytes = await _httpClient.GetByteArrayAsync(intlUrl);
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    await File.WriteAllBytesAsync(localPath, retryBytes);

                    var image = LoadImageSafe(localPath);
                    if (image != null)
                    {
                        _imageCache.TryAdd(cacheKey, image);
                        return new Bitmap(image);
                    }
                }
                catch (Exception retryEx)
                {
                    Debug.WriteLine($"[国际服重试失败] {retryEx.Message}");
                }
            }
            finally
            {
                _fileLock.Release();
            }

            return GetDefaultImageSafe();
        }

        /// <summary>
        /// 安全加载本地图片（核心修复点）
        /// </summary>
        private static Image? LoadImageSafe(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Seek(0, SeekOrigin.Begin);

                using var tempImage = Image.FromStream(ms);
                return new Bitmap(tempImage);   // 创建完全独立的副本
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LoadImageSafe失败] {path} → {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 每次都返回默认图片的副本，防止被外部 Dispose
        /// </summary>
        public static Image GetDefaultImageSafe()
        {
            return new Bitmap(_defaultProfile);
        }

        #region 版本号相关（保持不变）
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

        private static async Task<string> GetLatestDdragonVersionAsync(bool forceUpdate = false)
        {
            try
            {
                using var client = new HttpClient();
                var json = await client.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json");
                var versions = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                var latest = versions?.FirstOrDefault() ?? "15.12.1";

                Directory.CreateDirectory(Path.GetDirectoryName(VersionCachePath)!);
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
    }
}