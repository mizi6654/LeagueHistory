using League.Models;
using Newtonsoft.Json;

namespace League.Infrastructure
{
    public class SummonerSpellResourceLoader
    {
        private readonly HttpClient _client;
        private readonly ImageCacheService _imageCache;

        public List<SummonerSpell> SummonerSpells { get; } = new();

        public SummonerSpellResourceLoader(HttpClient client, ImageCacheService imageCache)
        {
            _client = client;
            _imageCache = imageCache;
        }

        public async Task LoadAsync()
        {
            string json = await _client.GetStringAsync("/lol-game-data/assets/v1/summoner-spells.json");
            var list = JsonConvert.DeserializeObject<List<dynamic>>(json);

            SummonerSpells.Clear();
            foreach (var s in list)
            {
                SummonerSpells.Add(new SummonerSpell
                {
                    Id = Convert.ToInt64(s.id),
                    Name = s.name?.ToString() ?? "",
                    Description = s.description?.ToString(),
                    IconPath = s.iconPath?.ToString() ?? "",
                    IconFileName = Path.GetFileName(s.iconPath?.ToString() ?? "")
                });
            }
        }

        public SummonerSpell? GetById(int spellId) =>
            SummonerSpells.FirstOrDefault(s => s.Id == spellId);

        public async Task<(Image? image, string name, string description)> GetInfoAsync(int spellId)
        {
            var spell = GetById(spellId);
            if (spell == null)
                return (null, "未知召唤师技能", "未找到该技能的描述");

            var image = await GetIconAsync(spell.IconPath);
            return (image, spell.Name, spell.Description);
        }

        public async Task<Image?> GetIconAsync(string iconPath)
        {
            if (string.IsNullOrEmpty(iconPath)) return null;
            if (!iconPath.StartsWith("/")) iconPath = "/" + iconPath;

            string fileName = Path.GetFileName(iconPath);
            if (string.IsNullOrEmpty(fileName)) return null;

            return await _imageCache.GetOrDownloadImageAsync(iconPath, "spell", fileName);
        }
    }
}