using League.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace League.Infrastructure
{
    public class AugmentResourceLoader
    {
        private readonly HttpClient _client;
        private readonly ImageCacheService _imageCache;

        private List<AugmentInfo> _augments = new();

        public AugmentResourceLoader(HttpClient client, ImageCacheService imageCache)
        {
            _client = client;
            _imageCache = imageCache;
        }

        public async Task LoadAsync()
        {
            try
            {
                var response = await _client.GetAsync("/lol-game-data/assets/v1/cherry-augments.json");
                if (!response.IsSuccessStatusCode)
                {
                    _augments = new();
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                var jsonArray = JArray.Parse(json);
                _augments = ParseAugmentsFromJsonArray(jsonArray);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"海克斯符文加载异常: {ex.Message}");
                _augments = new();
            }
        }

        private List<AugmentInfo> ParseAugmentsFromJsonArray(JArray jsonArray)
        {
            var augments = new List<AugmentInfo>();
            var descriptionMap = AugmentDescriptionRepository.GetAll();

            // 优先处理 ID >= 1000 的海克斯符文
            foreach (var item in jsonArray.Where(i => (i["id"]?.Value<int>() ?? 0) >= 1000))
                ProcessAugmentItem(item, augments, descriptionMap);

            foreach (var item in jsonArray.Where(i => (i["id"]?.Value<int>() ?? 0) < 1000))
                ProcessAugmentItem(item, augments, descriptionMap);

            return augments;
        }

        private void ProcessAugmentItem(JToken item, List<AugmentInfo> augments, Dictionary<string, string> descMap)
        {
            int id = item["id"]?.Value<int>() ?? 0;
            if (id <= 0) return;

            string name = item["nameTRA"]?.ToString() ?? item["name"]?.ToString() ?? $"Augment {id}";
            string description = item["description"]?.ToString() ?? item["desc"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(description) && descMap.TryGetValue(name, out var mapped))
                description = mapped;

            string iconPath = item["augmentSmallIconPath"]?.ToString() ?? "";

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(iconPath))
            {
                augments.Add(new AugmentInfo
                {
                    Id = id,
                    Name = name,
                    Description = description,
                    IconPath = iconPath,
                    Rarity = item["rarity"]?.ToString() ?? "Unknown"
                });
            }
        }

        public AugmentInfo? GetById(int id) => _augments.FirstOrDefault(a => a.Id == id);

        public async Task<(Image? image, string name, string description)> GetInfoAsync(int id)
        {
            var augment = GetById(id);
            if (augment == null)
                return (null, $"Augment {id}", "");

            string rarityCn = RarityToChinese(augment.Rarity);
            string cleanDesc = CleanDescription(augment.Description);
            string fullDesc = $"[{rarityCn}] {cleanDesc}";

            if (string.IsNullOrEmpty(augment.IconPath))
                return (null, augment.Name, fullDesc);

            var image = await _imageCache.GetOrDownloadImageAsync(
                augment.IconPath, "augments", $"{id}.png", id);

            return (image, augment.Name, fullDesc);
        }

        private static string CleanDescription(string desc) =>
            string.IsNullOrEmpty(desc) ? "" :
            desc.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();

        private static string RarityToChinese(string? rarity) => rarity?.ToLower() switch
        {
            "ksilver" => "白银",
            "kgold" => "黄金",
            "kprismatic" => "棱彩",
            _ => "未知"
        };
    }
}