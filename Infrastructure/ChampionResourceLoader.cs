using League.Models;
using Newtonsoft.Json;

namespace League.Infrastructure
{
    public class ChampionResourceLoader
    {
        private readonly HttpClient _client;
        private readonly ImageCacheService _imageCache;

        public List<Champion> Champions { get; } = new();

        public ChampionResourceLoader(HttpClient client, ImageCacheService imageCache)
        {
            _client = client;
            _imageCache = imageCache;
        }

        public async Task LoadAsync()
        {
            string json = await _client.GetStringAsync("/lol-game-data/assets/v1/champion-summary.json");
            var list = JsonConvert.DeserializeObject<List<dynamic>>(json);

            Champions.Clear();
            foreach (var c in list)
            {
                Champions.Add(new Champion
                {
                    Id = c.id,
                    Alias = c.alias,
                    Name = c.name,
                    Description = c.description
                });
            }
        }

        public Champion? GetById(int id) => Champions.FirstOrDefault(c => c.Id == id);

        public async Task<(Image? image, string name, string description)> GetInfoAsync(int id)
        {
            var champ = GetById(id);
            if (champ == null) return (null, "未知英雄", "未找到该英雄的描述");

            var image = await GetIconAsync(id);
            return (image, champ.Name, champ.Description);
        }

        public async Task<Image?> GetIconAsync(int id)
        {
            var champ = GetById(id);
            if (champ == null) return null;

            string safeName = champ.Name.Replace(" ", "").Replace("'", "");
            return await _imageCache.GetOrDownloadImageAsync(
                $"/lol-game-data/assets/v1/champion-icons/{id}.png", "champion", $"{safeName}.png");
        }
    }
}