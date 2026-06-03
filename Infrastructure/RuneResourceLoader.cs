using League.Models;
using Newtonsoft.Json;

namespace League.Infrastructure
{
    public class RuneResourceLoader
    {
        private readonly HttpClient _client;
        private readonly ImageCacheService _imageCache;

        public List<RuneInfo> Runes { get; private set; } = new();   // ← 改为 private set

        public RuneResourceLoader(HttpClient client, ImageCacheService imageCache)
        {
            _client = client;
            _imageCache = imageCache;
        }

        public async Task LoadAsync()
        {
            var response = await _client.GetAsync("/lol-game-data/assets/v1/perks.json");
            if (!response.IsSuccessStatusCode) return;

            string json = await response.Content.ReadAsStringAsync();
            Runes = JsonConvert.DeserializeObject<List<RuneInfo>>(json) ?? new List<RuneInfo>();
        }

        public RuneInfo? GetById(int runeId) => Runes.FirstOrDefault(r => r.id == runeId);

        public async Task<(Image? image, string name, string description)> GetInfoAsync(int runeId)
        {
            var rune = GetById(runeId);
            if (rune == null)
                return (null, "未知符文", "未找到该符文描述");

            var image = await GetIconAsync(rune.iconPath);
            return (image, rune.name, rune.longDesc);
        }

        public async Task<Image?> GetIconAsync(string iconPath)
        {
            if (string.IsNullOrEmpty(iconPath)) return null;

            string fileName = Path.GetFileName(iconPath);
            if (string.IsNullOrEmpty(fileName)) return null;

            return await _imageCache.GetOrDownloadImageAsync(iconPath, "runes", fileName);
        }
    }
}