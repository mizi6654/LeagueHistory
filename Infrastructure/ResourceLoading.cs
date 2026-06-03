using League.Clients;
using League.Models;
using League.PrimaryElection;
using System.Diagnostics;

namespace League.Infrastructure
{
    public class ResourceLoading
    {
        public HttpClient _lcuClient { get; private set; } = null!;

        public ChampionResourceLoader Champions { get; private set; }
        public ItemResourceLoader Items { get; private set; }
        public SummonerSpellResourceLoader SummonerSpells { get; private set; }
        public RuneResourceLoader Runes { get; private set; }
        public AugmentResourceLoader Augments { get; private set; }

        private ImageCacheService _imageCache;
        private ChampionManager? _championManager;

        public ResourceLoading()
        {
            _imageCache = new ImageCacheService(new HttpClient()); // 临时占位
            Champions = new ChampionResourceLoader(_imageCache._client, _imageCache); // 临时
            Items = new ItemResourceLoader(_imageCache._client, _imageCache);
            SummonerSpells = new SummonerSpellResourceLoader(_imageCache._client, _imageCache);
            Runes = new RuneResourceLoader(_imageCache._client, _imageCache);
            Augments = new AugmentResourceLoader(_imageCache._client, _imageCache);
        }

        public async void loadingResource(LcuSession _lcu)
        {
            _lcuClient = _lcu.Client;

            _imageCache.UpdateClient(_lcuClient);

            // 重新创建加载器，使用正确的 Client
            Champions = new ChampionResourceLoader(_lcuClient, _imageCache);
            Items = new ItemResourceLoader(_lcuClient, _imageCache);
            SummonerSpells = new SummonerSpellResourceLoader(_lcuClient, _imageCache);
            Runes = new RuneResourceLoader(_lcuClient, _imageCache);
            Augments = new AugmentResourceLoader(_lcuClient, _imageCache);

            await Task.WhenAll(
                Champions.LoadAsync(),
                Items.LoadAsync(),
                SummonerSpells.LoadAsync(),
                Runes.LoadAsync(),
                Augments.LoadAsync()
            );

            Debug.WriteLine("✅ 所有游戏资源加载完成");
        }

        #region ChampionManager
        public ChampionManager ChampionManager
        {
            get
            {
                if (_championManager == null)
                {
                    if (_lcuClient == null)
                        throw new InvalidOperationException("LcuClient未初始化");
                    _championManager = new ChampionManager(_lcuClient, this);
                }
                return _championManager;
            }
        }
        #endregion

        // 兼容原有调用方式
        public Champion? GetChampionById(int champId) => Champions.GetById(champId);
        public Task<(Image? image, string name, string description)> GetChampionInfoAsync(int champId) => Champions.GetInfoAsync(champId);
        public Task<Image?> GetChampionIconAsync(int champId) => Champions.GetIconAsync(champId);

        public Item? GetItemById(int itemId) => Items.GetById(itemId);
        public Task<(Image? image, string name, string description)> GetItemInfoAsync(int itemId) => Items.GetInfoAsync(itemId);
        public Task<Image?> GetItemIconAsync(string iconPath) => Items.GetIconAsync(iconPath);

        public SummonerSpell? GetSpellById(int spellId) => SummonerSpells.GetById(spellId);
        public Task<(Image? image, string name, string description)> GetSpellInfoAsync(int spellId) => SummonerSpells.GetInfoAsync(spellId);
        public Task<Image?> GetSummonerSpellIconAsync(string iconPath) => SummonerSpells.GetIconAsync(iconPath);

        public RuneInfo? GetRuneById(int runeId) => Runes.GetById(runeId);
        public Task<(Image? image, string name, string description)> GetRuneInfoAsync(int runeId) => Runes.GetInfoAsync(runeId);
        public Task<Image?> GetRuneIconAsync(string iconPath) => Runes.GetIconAsync(iconPath);

        public AugmentInfo? GetAugmentById(int id) => Augments.GetById(id);
        public Task<(Image? image, string name, string description)> GetAugmentInfoAsync(int id) => Augments.GetInfoAsync(id);
    }
}