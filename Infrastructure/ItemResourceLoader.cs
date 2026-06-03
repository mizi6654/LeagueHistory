using League.Models;
using Newtonsoft.Json;

namespace League.Infrastructure
{
    public class ItemResourceLoader
    {
        private readonly HttpClient _client;
        private readonly ImageCacheService _imageCache;

        public List<Item> Items { get; } = new();

        public ItemResourceLoader(HttpClient client, ImageCacheService imageCache)
        {
            _client = client;
            _imageCache = imageCache;
        }

        public async Task LoadAsync()
        {
            string json = await _client.GetStringAsync("/lol-game-data/assets/v1/items.json");
            var list = JsonConvert.DeserializeObject<List<dynamic>>(json);

            Items.Clear();
            foreach (var i in list)
            {
                string iconPath = (string?)i.iconPath;
                if (string.IsNullOrEmpty(iconPath)) continue;

                Items.Add(new Item
                {
                    Id = i.id,
                    Name = i.name,
                    Description = i.description,
                    IconFileName = iconPath
                });
            }
        }

        public Item? GetById(int itemId) => Items.FirstOrDefault(i => i.Id == itemId);

        public async Task<(Image? image, string name, string description)> GetInfoAsync(int itemId)
        {
            var item = GetById(itemId);
            if (item == null)
                return (null, "未知装备", "未找到该装备的描述");

            var image = await GetIconAsync(item.IconFileName);
            return (image, item.Name, item.Description);
        }

        public async Task<Image?> GetIconAsync(string iconPath)
        {
            string fileName = Path.GetFileName(iconPath);
            if (string.IsNullOrEmpty(fileName)) return null;

            return await _imageCache.GetOrDownloadImageAsync(iconPath, "item", fileName);
        }
    }
}