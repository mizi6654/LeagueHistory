using League.Infrastructure;
using Newtonsoft.Json;
using System.Diagnostics;

namespace League.PrimaryElection
{
    /// <summary>
    /// 英雄管理器：负责从 DDragon 或本地缓存加载英雄数据，并提供查询接口
    /// </summary>
    public class ChampionManager
    {
        private readonly HttpClient _lcuClient;
        private readonly ResourceLoading _resourceLoader;
        private List<PrimaryChampion> _allChampions = new List<PrimaryChampion>();
        private bool _isInitialized = false;

        private readonly string _cacheFilePath = "primary_champion_cache.json";
        private readonly string _versionFilePath = Path.Combine("Assets", "ddragon_version.txt");

        public event EventHandler ChampionsLoaded;
        public event EventHandler<string> LoadError;

        public List<PrimaryChampion> AllChampions => _allChampions;
        public bool IsInitialized => _isInitialized;

        public ChampionManager(HttpClient lcuClient, ResourceLoading resourceLoader)
        {
            _lcuClient = lcuClient;
            _resourceLoader = resourceLoader;
        }

        /// <summary>
        /// 异步初始化英雄数据（优先使用缓存 + 版本校验）
        /// </summary>
        /// <param name="forceRefresh">是否强制刷新（忽略缓存，直接联网）</param>
        public async Task InitializeAsync(bool forceRefresh = false)
        {
            try
            {
                if (_isInitialized && !forceRefresh)
                    return;

                Debug.WriteLine("[ChampionManager] 开始加载英雄数据...");

                // 读取本地版本文件（如 Assets/ddragon_version.txt）
                string localVersion = ReadLocalVersion();

                // 非强制刷新时尝试加载缓存
                if (!forceRefresh && await TryLoadFromCacheAsync(localVersion))
                {
                    _isInitialized = true;
                    ChampionsLoaded?.Invoke(this, EventArgs.Empty);
                    ValidatePositionMapping();
                    return;
                }

                // 缓存无效或强制刷新 → 从 DDragon 联网加载
                await LoadFromDDragonAsync();

                // 保存新缓存（带版本号）
                await SaveToCacheAsync(localVersion);

                _isInitialized = true;
                ChampionsLoaded?.Invoke(this, EventArgs.Empty);
                ValidatePositionMapping();

                Debug.WriteLine($"[ChampionManager] 英雄数据加载完成，共 {_allChampions.Count} 个英雄");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChampionManager] 初始化失败: {ex.Message}");
                LoadError?.Invoke(this, ex.Message);
            }
        }

        /// <summary>
        /// 读取本地版本文件内容
        /// </summary>
        private string ReadLocalVersion()
        {
            try
            {
                if (File.Exists(_versionFilePath))
                {
                    string version = File.ReadAllText(_versionFilePath).Trim();
                    Debug.WriteLine($"[ChampionManager] 本地版本文件读取: {version}");
                    return version;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChampionManager] 读取本地版本文件失败: {ex.Message}");
            }
            return string.Empty;
        }

        /// <summary>
        /// 验证所有英雄是否在位置映射表中（用于调试，方便发现缺失英雄）
        /// </summary>
        private void ValidatePositionMapping()
        {
            Debug.WriteLine("[ChampionManager] 验证英雄位置映射...");

            int mappedCount = 0;
            int unmappedCount = 0;

            foreach (var champion in _allChampions)
            {
                var positions = ChampionPositionMapper.GetPositionsByChampionName(champion.Name);
                if (positions.Count > 0)
                {
                    mappedCount++;
                }
                else
                {
                    unmappedCount++;
                    Debug.WriteLine($"[未映射] {champion.Name}");
                }
            }

            Debug.WriteLine($"[ChampionManager] 映射统计: {mappedCount}个已映射, {unmappedCount}个未映射");

            if (unmappedCount > 0)
            {
                Debug.WriteLine("[ChampionManager] 未映射的英雄:");
                foreach (var champion in _allChampions)
                {
                    var positions = ChampionPositionMapper.GetPositionsByChampionName(champion.Name);
                    if (positions.Count == 0)
                    {
                        Debug.WriteLine($"  {champion.Name} (ID:{champion.Id}, Alias:{champion.Alias})");
                    }
                }
            }
        }

        /// <summary>
        /// 从 DataDragon 官方接口加载最新英雄数据
        /// </summary>
        private async Task LoadFromDDragonAsync()
        {
            try
            {
                // 创建一个独立的 HttpClient，超时时间设长一些（100秒默认即可）
                using var handler = new HttpClientHandler();
                using var httpClient = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(60)  // 60秒超时，足够宽松
                };

                // 添加常见 User-Agent，避免被某些 CDN 限流（可选但推荐）
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                const string ddragonUrl = "https://ddragon.leagueoflegends.com/cdn/15.24.1/data/zh_CN/champion.json";

                var json = await httpClient.GetStringAsync(ddragonUrl);

                var ddragonData = JsonConvert.DeserializeObject<DDragonChampionRoot>(json);

                if (ddragonData?.Data == null)
                    throw new Exception("DDragon 数据为空");

                _allChampions.Clear();

                foreach (var kvp in ddragonData.Data)
                {
                    var champData = kvp.Value;
                    var champion = new PrimaryChampion
                    {
                        Id = int.Parse(champData.Key),
                        Name = champData.Name,
                        Title = champData.Title,
                        Alias = champData.Id,
                        Description = champData.Blurb ?? string.Empty,
                        Tags = champData.Tags ?? new List<string>(),
                        IsActive = true
                    };
                    _allChampions.Add(champion);
                }

                _allChampions = _allChampions.OrderBy(c => c.Name).ToList();

                Debug.WriteLine($"[ChampionManager] 从 DDragon 加载了 {_allChampions.Count} 个英雄");
            }
            catch (TaskCanceledException tcex)
            {
                throw new Exception("DDragon 请求超时或被取消，请检查网络连接或尝试稍后重试。", tcex);
            }
            catch (Exception ex)
            {
                throw new Exception($"从 DDragon 加载英雄数据失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 尝试从本地缓存加载英雄数据（需版本匹配）
        /// </summary>
        private async Task<bool> TryLoadFromCacheAsync(string localVersion)
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                    return false;

                var json = await File.ReadAllTextAsync(_cacheFilePath);
                var cachedData = JsonConvert.DeserializeObject<PrimaryChampionCacheDto>(json);

                if (cachedData == null || cachedData.Champions == null || cachedData.Champions.Count == 0)
                    return false;

                // 版本不匹配则视为缓存无效
                if (!string.Equals(cachedData.DataVersion, localVersion, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[ChampionManager] 缓存版本 {cachedData.DataVersion} 与本地 {localVersion} 不匹配，需刷新");
                    return false;
                }

                _allChampions = cachedData.Champions;
                Debug.WriteLine($"[ChampionManager] 从缓存加载了 {_allChampions.Count} 个英雄（版本 {localVersion}）");

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChampionManager] 缓存加载失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将当前英雄数据保存到本地缓存（带版本号）
        /// </summary>
        private async Task SaveToCacheAsync(string currentVersion)
        {
            try
            {
                var cacheData = new PrimaryChampionCacheDto
                {
                    DataVersion = currentVersion,
                    CacheTime = DateTime.Now,
                    Champions = _allChampions
                };

                var json = JsonConvert.SerializeObject(cacheData, Formatting.Indented);
                await File.WriteAllTextAsync(_cacheFilePath, json);

                Debug.WriteLine($"[ChampionManager] 已缓存 {_allChampions.Count} 个英雄数据（版本 {currentVersion}）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChampionManager] 缓存保存失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据位置筛选活跃英雄（完全依赖 ChampionPositionMapper）
        /// </summary>
        public List<PrimaryChampion> GetChampionsByPosition(ChampionPosition position)
        {
            if (position == ChampionPosition.All)
                return _allChampions.Where(c => c.IsActive).ToList();

            return _allChampions
                .Where(c => c.IsActive && c.GetPositions().Contains(position))
                .OrderBy(c => c.Name)
                .ToList();
        }

        /// <summary>
        /// 根据英雄ID获取英雄对象（用于从配置恢复预选列表时查找英雄名称和位置）
        /// </summary>
        /// <param name="championId">英雄ID</param>
        /// <returns>对应的 PrimaryChampion 对象，如果未找到返回 null</returns>
        public PrimaryChampion GetChampionById(int championId)
        {
            return _allChampions.FirstOrDefault(c => c.Id == championId);
        }

        /// <summary>
        /// 异步获取英雄图标（通过 ResourceLoading）
        /// </summary>
        public async Task<Image> GetChampionIconAsync(int championId)
        {
            return await _resourceLoader.GetChampionIconAsync(championId);
        }
    }

    /// <summary>
    /// 缓存数据传输对象
    /// </summary>
    public class PrimaryChampionCacheDto
    {
        public string DataVersion { get; set; } = string.Empty; // DDragon 数据版本
        public DateTime CacheTime { get; set; }
        public List<PrimaryChampion> Champions { get; set; } = new List<PrimaryChampion>();
    }

    /// <summary>
    /// DDragon 英雄列表根对象
    /// </summary>
    public class DDragonChampionRoot
    {
        public Dictionary<string, DDragonChampionData> Data { get; set; }
    }

    /// <summary>
    /// DDragon 单个英雄数据
    /// </summary>
    public class DDragonChampionData
    {
        public string Key { get; set; }     // 英雄ID（如 "72"）
        public string Id { get; set; }      // 英文名（如 "Skarner"）
        public string Name { get; set; }    // 中文名（如 "上古领主"）
        public string Title { get; set; }   // 称号（如 "斯卡纳"）
        public string Blurb { get; set; }   // 简述
        public List<string> Tags { get; set; }
    }
}