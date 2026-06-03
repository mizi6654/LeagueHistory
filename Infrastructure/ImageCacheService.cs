using System.Diagnostics;

namespace League.Infrastructure
{
    public class ImageCacheService
    {
        public HttpClient _client;
        private readonly string _baseCacheDir;

        public ImageCacheService(HttpClient client, string baseCacheDir = "Assets")
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _baseCacheDir = baseCacheDir;
        }

        public void UpdateClient(HttpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<Image?> GetOrDownloadImageAsync(string iconPath, string cacheSubDir, string fileName, int? idForNaming = null)
        {
            string cacheDir = Path.Combine(Application.StartupPath, _baseCacheDir, cacheSubDir);
            Directory.CreateDirectory(cacheDir);

            string cachePath = idForNaming.HasValue
                ? Path.Combine(cacheDir, $"{idForNaming}.png")
                : Path.Combine(cacheDir, fileName);

            if (File.Exists(cachePath))
            {
                try
                {
                    using var stream = File.OpenRead(cachePath);
                    return (Image)Image.FromStream(stream).Clone();
                }
                catch { }
            }

            try
            {
                var response = await _client.GetAsync(iconPath);
                if (!response.IsSuccessStatusCode) return null;

                using var stream = await response.Content.ReadAsStreamAsync();
                var image = Image.FromStream(stream);

                await SaveImageToCacheAsync(image, cachePath);
                return image;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"图片加载失败 {iconPath}: {ex.Message}");
                return null;
            }
        }

        private async Task SaveImageToCacheAsync(Image image, string cachePath)
        {
            string tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";

            try
            {
                int retry = 0;
                while (retry < 3)
                {
                    try
                    {
                        using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
                        await Task.Run(() => image.Save(fs, System.Drawing.Imaging.ImageFormat.Png));
                        fs.Flush();
                        break;
                    }
                    catch (IOException) when (retry < 2)
                    {
                        retry++;
                        await Task.Delay(100 * retry);
                    }
                }

                if (File.Exists(cachePath)) File.Delete(cachePath);
                File.Move(tempPath, cachePath);
            }
            finally
            {
                if (File.Exists(tempPath)) TryDelete(tempPath);
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}