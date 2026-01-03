using League.Clients;
using League.Models;
using League.PrimaryElection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace League.Infrastructure
{
    
    public class ResourceLoading
    {
        public HttpClient _lcuClient;

        public List<Champion> Champions = new List<Champion>(); //存储英雄名称
        public List<Item> Items = new List<Item>(); //存储装备名称
        public List<SummonerSpell> SummonerSpells = new List<SummonerSpell>();  //存储召唤师名称
        public List<RuneInfo> Runes = new List<RuneInfo>(); //存储符文技能名称
        public List<ProfileIcon> ProfileIcons = new List<ProfileIcon>();   //存储玩家头像信息
        private List<AugmentInfo> _augments;    //海克斯符文

        // 添加ChampionManager
        private ChampionManager _championManager;

        public async void loadingResource(LcuSession _lcu) 
        {
            _lcuClient = _lcu.Client; // 构造函数中可以安全引用

            await LoadChampionsAsync();
            await LoadItemsAsync();
            await LoadSpellsAsync();
            await LoadRunesAsync();
            await LoadAugmentsAsync();
        }

        #region 预选英雄处理
        public ChampionManager ChampionManager
        {
            get
            {
                if (_championManager == null)
                {
                    // 确保_lcuClient已经初始化
                    if (_lcuClient == null)
                        throw new InvalidOperationException("LcuClient未初始化");

                    _championManager = new ChampionManager(_lcuClient, this);
                }
                return _championManager;
            }
        }
        #endregion

        #region 加载所有英雄

        /// <summary>
        /// 初始化所有英雄数据
        /// </summary>
        /// <returns></returns>
        public async Task LoadChampionsAsync()
        {
            string json = await _lcuClient.GetStringAsync("/lol-game-data/assets/v1/champion-summary.json");
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

        /// <summary>
        /// 根据Id查找英雄映射
        /// </summary>
        /// <param name="champId"></param>
        /// <returns></returns>
        public Champion GetChampionById(int champId)
        {
            return Champions.FirstOrDefault(c => c.Id == champId);
        }

        /// <summary>
        /// 根据英雄Id获取完整信息（图片 + 名称 + 描述）
        /// </summary>
        /// <param name="champId"></param>
        /// <returns></returns>
        public async Task<(Image image, string name, string description)> GetChampionInfoAsync(int champId)
        {
            var champion = GetChampionById(champId);
            if (champion != null)
            {
                var image = await GetChampionIconAsync(champId);
                return (image, champion.Name, champion.Description);
            }

            return (null, "未知英雄", "未找到该英雄的描述");
        }

        /// <summary>
        /// 根据ID获取图片并缓存本地
        /// </summary>
        /// <param name="champId"></param>
        /// <param name="champion"></param>
        /// <returns></returns>
        public async Task<Image> GetChampionIconAsync(int champId)
        {
            // 确保缓存目录存在
            string cacheDir = Path.Combine(Application.StartupPath, "Assets", "champion");
            Directory.CreateDirectory(cacheDir); // 如果目录不存在则创建

            // 获取英雄信息用于构建文件名
            var champion = GetChampionById(champId);
            if (champion == null)
            {
                //Debug.WriteLine($"未找到ID为 {champId} 的英雄");
                return null;
            }

            // 构建本地缓存路径
            string safeName = champion.Name.Replace(" ", "").Replace("'", "");
            string cachePath = Path.Combine(cacheDir, $"{safeName}.png");

            // 1. 首先尝试从本地缓存加载
            if (File.Exists(cachePath))
            {
                try
                {
                    using (var stream = File.OpenRead(cachePath))
                    {
                        var image = Image.FromStream(stream);
                        //Debug.WriteLine($"从本地缓存加载英雄图标: {champion.Name}");
                        return (Image)image.Clone(); // 返回克隆对象以避免资源释放问题
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"本地缓存加载失败: {ex.Message}");
                    // 如果缓存文件损坏，继续从网络获取
                }
            }

            // 2. 本地没有则从LCU API获取
            string path = $"/lol-game-data/assets/v1/champion-icons/{champId}.png";
            try
            {
                var response = await _lcuClient.GetAsync(path);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"图片获取失败，状态码: {response.StatusCode}");
                    return null;
                }

                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var image = Image.FromStream(stream);
                    Debug.WriteLine($"成功从API读取图片: {champion.Name}");

                    // 3. 将获取的图片保存到本地缓存
                    try
                    {
                        await SaveImageToCacheAsync(image, cachePath);
                        Debug.WriteLine($"已缓存英雄图标: {champion.Name}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"缓存保存失败: {ex.Message}");
                        // 即使缓存失败也返回图片
                    }

                    return image;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"图片加载异常: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 将图片保存到本地缓存
        /// </summary>
        /// <param name="image"></param>
        /// <param name="cachePath"></param>
        /// <returns></returns>
        private async Task SaveImageToCacheAsync(Image image, string cachePath)
        {
            // 确保目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));

            // 使用带随机后缀的临时文件，避免多线程冲突
            string tempPath = $"{cachePath}.{Guid.NewGuid().ToString("N").Substring(0, 8)}.tmp";

            try
            {
                // 尝试多次写入（应对可能的临时冲突）
                int retryCount = 0;
                const int maxRetries = 3;

                while (retryCount < maxRetries)
                {
                    try
                    {
                        // 使用FileShare.None确保独占访问
                        using (var fs = new FileStream(
                            tempPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 4096,
                            useAsync: true))
                        {
                            await Task.Run(() =>
                            {
                                image.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
                                fs.Flush(); // 确保所有数据写入磁盘
                            });
                        }
                        break; // 成功则退出重试循环
                    }
                    catch (IOException) when (retryCount < maxRetries - 1)
                    {
                        retryCount++;
                        await Task.Delay(100 * retryCount); // 指数退避
                        continue;
                    }
                }

                // 原子性替换文件
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }
                File.Move(tempPath, cachePath);
            }
            finally
            {
                // 确保临时文件被清理
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (IOException)
                    {
                        // 如果删除失败，可以记录日志但不影响主流程
                        Debug.WriteLine($"无法删除临时文件: {tempPath}");
                    }
                }
            }
        }
        #endregion

        #region 加载所有装备

        /// <summary>
        /// 初始化所有装备信息
        /// </summary>
        /// <returns></returns>
        public async Task LoadItemsAsync()
        {
            string json = await _lcuClient.GetStringAsync("/lol-game-data/assets/v1/items.json");
            var list = JsonConvert.DeserializeObject<List<dynamic>>(json);
            Items.Clear();

            foreach (var i in list)
            {
                int id = i.id;
                string name = i.name;
                string description = i.description;
                string iconPath = (string)i.iconPath;

                if (string.IsNullOrEmpty(iconPath)) continue;

                Items.Add(new Item
                {
                    Id = id,
                    Name = name,
                    Description = description,
                    IconFileName = iconPath // 注意：这里是完整路径
                });
            }
        }

        /// <summary>
        /// 根据装备ID查找装备映射
        /// </summary>
        /// <param name="itemId"></param>
        /// <returns></returns>
        public Item GetItemById(int itemId)
        {
            return Items.FirstOrDefault(i => i.Id == itemId);
        }

        /// <summary>
        /// 根据装备ID查找（图片、名称、描述）
        /// </summary>
        /// <param name="itemId"></param>
        /// <returns></returns>
        public async Task<(Image image, string name, string description)> GetItemInfoAsync(int itemId)
        {
            var item = GetItemById(itemId);
            if (item != null)
            {
                var image = await GetItemIconAsync(item.IconFileName);
                return (image, item.Name, item.Description);
            }

            return (null, "未知装备", "未找到该装备的描述");
        }

        /// <summary>
        /// 根据装备路径获取图片并缓存本地
        /// </summary>
        /// <param name="iconPath"></param>
        /// <returns></returns>
        public async Task<Image> GetItemIconAsync(string iconPath)
        {
            string cacheDir = Path.Combine(Application.StartupPath, "Assets", "item");
            Directory.CreateDirectory(cacheDir);

            string fileName = Path.GetFileName(iconPath);
            if (string.IsNullOrEmpty(fileName))
            {
                Debug.WriteLine($"无效的装备图标路径: {iconPath}");
                return null;
            }

            string cachePath = Path.Combine(cacheDir, fileName);

            if (File.Exists(cachePath))
            {
                try
                {
                    using (var stream = File.OpenRead(cachePath))
                    {
                        var image = Image.FromStream(stream);
                        return (Image)image.Clone(); // clone避免流关闭后图片失效
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"本地缓存加载失败: {ex.Message}");
                }
            }

            try
            {
                var response = await _lcuClient.GetAsync(iconPath).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"装备图标获取失败: {iconPath} 状态码: {response.StatusCode}");
                    return null;
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    Image image;
                    try
                    {
                        image = Image.FromStream(stream);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"图片流转换失败: {ex.Message}");
                        return null;
                    }

                    try
                    {
                        await SaveItemImageToCacheAsync(image, cachePath).ConfigureAwait(false);
                        Debug.WriteLine($"已缓存装备图标: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"缓存图片失败: {ex.Message}");
                    }

                    return image;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"装备图标加载异常: {ex}");
                return null;
            }
        }

        private async Task SaveItemImageToCacheAsync(Image image, string cachePath)
        {
            // 确保目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));

            // 使用唯一临时文件名
            string tempPath = $"{cachePath}.{Guid.NewGuid().ToString("N").Substring(0, 8)}.tmp";

            try
            {
                // 重试机制
                int retryCount = 0;
                const int maxRetries = 3;

                while (retryCount < maxRetries)
                {
                    try
                    {
                        using (var fs = new FileStream(
                            tempPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 4096,
                            useAsync: true))
                        {
                            await Task.Run(() =>
                            {
                                image.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
                                fs.Flush();
                            });
                        }
                        break;
                    }
                    catch (IOException) when (retryCount < maxRetries - 1)
                    {
                        retryCount++;
                        await Task.Delay(100 * retryCount);
                        continue;
                    }
                }

                // 原子性替换文件
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }
                File.Move(tempPath, cachePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch (IOException ex)
                    {
                        Debug.WriteLine($"无法删除临时文件: {ex.Message}");
                    }
                }
            }
        }
        #endregion

        #region 加载所有召唤师技能
        public async Task LoadSpellsAsync()
        {
            string json = await _lcuClient.GetStringAsync("/lol-game-data/assets/v1/summoner-spells.json");
            var list = JsonConvert.DeserializeObject<List<dynamic>>(json);
            SummonerSpells.Clear();

            foreach (var s in list)
            {
                long id = Convert.ToInt64(s.id); // 改为 long
                string name = s.name.ToString();
                string description = s.description?.ToString();
                string iconPath = s.iconPath.ToString();
                string iconFile = Path.GetFileName(iconPath);

                SummonerSpells.Add(new SummonerSpell
                {
                    Id = id,
                    Name = name,
                    Description = description,
                    IconPath = iconPath,
                    IconFileName = iconFile
                });
            }
        }

        //查找方法
        public SummonerSpell GetSpellById(int spellId)
        {
            return SummonerSpells.FirstOrDefault(s => s.Id == spellId);
        }

        //获取图片+描述方法
        public async Task<(Image image, string name, string description)> GetSpellInfoAsync(int spellId)
        {
            var spell = GetSpellById(spellId);
            if (spell != null)
            {
                var image = await GetSummonerSpellIconAsync(spell.IconPath);
                return (image, spell.Name, spell.Description);
            }

            return (null, "未知召唤师技能", "未找到该技能的描述");
        }

        public async Task<Image> GetSummonerSpellIconAsync(string iconPath)
        {
            // 统一路径格式处理
            if (!iconPath.StartsWith("/"))
                iconPath = "/" + iconPath;

            // 确保缓存目录存在
            string cacheDir = Path.Combine(Application.StartupPath, "Assets", "spell");
            Directory.CreateDirectory(cacheDir);

            // 从路径中提取文件名
            string fileName = Path.GetFileName(iconPath);
            if (string.IsNullOrEmpty(fileName))
            {
                Debug.WriteLine($"无效的召唤师技能图标路径: {iconPath}");
                return null;
            }

            // 构建本地缓存路径
            string cachePath = Path.Combine(cacheDir, fileName);

            // 1. 首先尝试从本地缓存加载
            if (File.Exists(cachePath))
            {
                try
                {
                    using (var stream = File.OpenRead(cachePath))
                    {
                        var image = Image.FromStream(stream);
                        //Debug.WriteLine($"从本地缓存加载召唤师技能图标: {fileName}");
                        return (Image)image.Clone();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"本地缓存加载失败: {ex.Message}");
                    // 如果缓存文件损坏，继续从网络获取
                }
            }

            // 2. 本地没有则从LCU API获取
            try
            {
                var response = await _lcuClient.GetAsync(iconPath);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"召唤师技能图标获取失败: {iconPath} 状态码: {response.StatusCode}");
                    return null;
                }

                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var image = Image.FromStream(stream);
                    Debug.WriteLine($"成功从API读取召唤师技能图标: {fileName}");

                    // 3. 将获取的图片保存到本地缓存
                    try
                    {
                        await SaveSpellImageToCacheAsync(image, cachePath);
                        Debug.WriteLine($"已缓存召唤师技能图标: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"召唤师技能图标缓存保存失败: {ex.Message}");
                        // 即使缓存失败也返回图片
                    }

                    return image;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"召唤师技能图标加载异常: {ex}");
                return null;
            }
        }

        private async Task SaveSpellImageToCacheAsync(Image image, string cachePath)
        {
            // 确保目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));

            // 使用唯一临时文件名
            string tempPath = $"{cachePath}.{Guid.NewGuid().ToString("N").Substring(0, 8)}.tmp";

            try
            {
                // 重试机制
                int retryCount = 0;
                const int maxRetries = 3;

                while (retryCount < maxRetries)
                {
                    try
                    {
                        using (var fs = new FileStream(
                            tempPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 4096,
                            useAsync: true))
                        {
                            await Task.Run(() =>
                            {
                                image.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
                                fs.Flush();
                            });
                        }
                        break;
                    }
                    catch (IOException) when (retryCount < maxRetries - 1)
                    {
                        retryCount++;
                        await Task.Delay(100 * retryCount);
                        continue;
                    }
                }

                // 原子性替换文件
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }
                File.Move(tempPath, cachePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch (IOException ex)
                    {
                        Debug.WriteLine($"无法删除临时文件: {ex.Message}");
                    }
                }
            }
        }
        #endregion

        #region 加载所有符文
        public async Task LoadRunesAsync()
        {
            var path = "/lol-game-data/assets/v1/perks.json";
            try
            {
                var response = await _lcuClient.GetAsync(path);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"符文数据获取失败: {response.StatusCode}");
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                Runes = JsonConvert.DeserializeObject<List<RuneInfo>>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"符文加载异常: {ex.Message}");
            }
        }

        //查找方法
        public RuneInfo GetRuneById(int runeId)
        {
            return Runes.FirstOrDefault(r => r.id == runeId);
        }

        //获取符文图片与描述
        public async Task<(Image image, string name, string description)> GetRuneInfoAsync(int runeId)
        {
            var rune = GetRuneById(runeId);
            if (rune != null)
            {
                //Debug.WriteLine($"找到符文: {runeId} => {rune.name}");
                var image = await GetRuneIconAsync(rune.iconPath);
                return (image, rune.name, rune.longDesc);
            }
            Debug.WriteLine($"未找到符文: {runeId}");
            return (null, "未知符文", "未找到该符文描述");
        }

        public async Task<Image> GetRuneIconAsync(string iconPath)
        {
            // 确保缓存目录存在
            string cacheDir = Path.Combine(Application.StartupPath, "Assets", "runes");
            Directory.CreateDirectory(cacheDir);

            // 从路径中提取文件名
            string fileName = Path.GetFileName(iconPath);
            if (string.IsNullOrEmpty(fileName))
            {
                Debug.WriteLine($"无效的符文图标路径: {iconPath}");
                return null;
            }

            // 构建本地缓存路径
            string cachePath = Path.Combine(cacheDir, fileName);

            // 1. 首先尝试从本地缓存加载
            if (File.Exists(cachePath))
            {
                try
                {
                    using (var stream = File.OpenRead(cachePath))
                    {
                        var image = Image.FromStream(stream);
                        //Debug.WriteLine($"从本地缓存加载符文图标: {fileName}");
                        return (Image)image.Clone();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"本地缓存加载失败: {ex.Message}");
                    // 如果缓存文件损坏，继续从网络获取
                }
            }

            // 2. 本地没有则从LCU API获取
            try
            {
                var response = await _lcuClient.GetAsync(iconPath);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"符文图标获取失败: {iconPath} 状态码: {response.StatusCode}");
                    return null;
                }

                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var image = Image.FromStream(stream);
                    Debug.WriteLine($"成功从API读取符文图标: {fileName}");

                    // 3. 将获取的图片保存到本地缓存
                    try
                    {
                        await SaveRuneImageToCacheAsync(image, cachePath);
                        Debug.WriteLine($"已缓存符文图标: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"符文图标缓存保存失败: {ex.Message}");
                        // 即使缓存失败也返回图片
                    }

                    return image;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"符文图标加载异常: {ex}");
                return null;
            }
        }

        private async Task SaveRuneImageToCacheAsync(Image image, string cachePath)
        {
            // 确保目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));

            // 使用唯一临时文件名
            string tempPath = $"{cachePath}.{Guid.NewGuid().ToString("N").Substring(0, 8)}.tmp";

            try
            {
                // 重试机制
                int retryCount = 0;
                const int maxRetries = 3;

                while (retryCount < maxRetries)
                {
                    try
                    {
                        using (var fs = new FileStream(
                            tempPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 4096,
                            useAsync: true))
                        {
                            await Task.Run(() =>
                            {
                                image.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
                                fs.Flush();
                            });
                        }
                        break;
                    }
                    catch (IOException) when (retryCount < maxRetries - 1)
                    {
                        retryCount++;
                        await Task.Delay(100 * retryCount);
                        continue;
                    }
                }

                // 原子性替换文件
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }
                File.Move(tempPath, cachePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch (IOException ex)
                    {
                        Debug.WriteLine($"无法删除临时文件: {ex.Message}");
                    }
                }
            }
        }
        #endregion

        #region 海克斯符文
        // 预加载海克斯符文数据
        public async Task LoadAugmentsAsync()
        {
            try
            {
                var path = "/lol-game-data/assets/v1/cherry-augments.json";

                //Debug.WriteLine($"加载LCU海克斯数据: {path}");
                var response = await _lcuClient.GetAsync(path);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"海克斯数据获取失败: {response.StatusCode}");
                    _augments = new List<AugmentInfo>();
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                var jsonArray = JArray.Parse(json);

                _augments = ParseAugmentsFromJsonArray(jsonArray);
                //Debug.WriteLine($"✅ 海克斯符文加载完成: {_augments.Count} 个");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"海克斯符文加载异常: {ex.Message}");
                _augments = new List<AugmentInfo>();
            }
        }

        // 从JSON数组解析海克斯符文
        private List<AugmentInfo> ParseAugmentsFromJsonArray(JArray jsonArray)
        {
            var augments = new List<AugmentInfo>();

            try
            {
                // 先初始化描述映射
                var descriptionMap = CreateAugmentDescriptionMap();

                // 第一遍：优先处理ID 1000及以上的海克斯大乱斗符文
                foreach (var item in jsonArray)
                {
                    try
                    {
                        int id = item["id"]?.Value<int>() ?? 0;
                        if (id < 1000) continue; // 跳过ID 1000以下的

                        ProcessAugmentItem(item, augments, descriptionMap);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"解析海克斯符文异常 ID {item["id"]}: {ex.Message}");
                    }
                }

                // 第二遍：处理ID 1000以下的符文（如果有的话）
                foreach (var item in jsonArray)
                {
                    try
                    {
                        int id = item["id"]?.Value<int>() ?? 0;
                        if (id >= 1000) continue; // 跳过已经处理过的

                        ProcessAugmentItem(item, augments, descriptionMap);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"解析海克斯符文异常 ID {item["id"]}: {ex.Message}");
                    }
                }

                Debug.WriteLine($"✅ 海克斯符文解析完成: {augments.Count} 个 (优先处理ID≥1000的符文)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解析海克斯符文数据时发生异常: {ex.Message}");
                Debug.WriteLine($"堆栈跟踪: {ex.StackTrace}");
            }

            return augments;
        }

        // 处理单个符文项目的辅助方法
        private void ProcessAugmentItem(JToken item, List<AugmentInfo> augments, Dictionary<string, string> descriptionMap)
        {
            int id = item["id"]?.Value<int>() ?? 0;
            if (id <= 0) return;

            string name = item["nameTRA"]?.ToString()
                         ?? item["name"]?.ToString()
                         ?? $"Augment {id}";

            string description = item["description"]?.ToString()
                                ?? item["desc"]?.ToString()
                                ?? "";

            // 如果JSON中没有描述，尝试从映射中获取
            if (string.IsNullOrEmpty(description) && descriptionMap.ContainsKey(name))
            {
                description = descriptionMap[name];
                //Debug.WriteLine($"使用映射描述: {name} -> {description.Substring(0, Math.Min(20, description.Length))}...");
            }

            string iconPath = item["augmentSmallIconPath"]?.ToString();

            // 读取稀有度
            string rarity = item["rarity"]?.ToString() ?? "Unknown";

            // 只要有ID、名称和图标路径就认为是有效符文
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(iconPath))
            {
                augments.Add(new AugmentInfo
                {
                    Id = id,
                    Name = name,
                    Description = description,
                    IconPath = iconPath,
                    Rarity = rarity
                });
            }
        }

        // 创建描述映射字典
        private Dictionary<string, string> CreateAugmentDescriptionMap()
        {
            // 使用不区分大小写的比较器
            var descriptionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 使用 HashSet 来跟踪已添加的键，避免重复
            var addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 辅助方法，避免重复添加
            void AddDescription(string key, string value)
            {
                if (!addedKeys.Contains(key))
                {
                    descriptionMap[key] = value;
                    addedKeys.Add(key);
                }
                else
                {
                    Debug.WriteLine($"警告：跳过重复的符文名称: {key}");
                }
            }

            // === 白银符文描述映射 ===
            AddDescription("物理转魔法", "将额外攻击力转化为法术强度，并获得法术强度。");
            AddDescription("大力", "获得攻击力。");
            AddDescription("霸符兄弟", "你获得【余烬之冠】和【洞悉之冠】。");
            AddDescription("灵巧", "获得攻击速度。");
            AddDescription("俯冲轰炸", "在你阵亡时爆炸，对附近的敌人们造成真实伤害。");
            AddDescription("唯快不破", "你对比你移动速度慢的目标，多造成额外伤害。");
            AddDescription("侵蚀", "对敌人造成伤害时会施加护甲和魔法抗性击碎效果。");
            AddDescription("急救用具", "获得治疗和护盾强度。");
            AddDescription("闪光弹", "闪现时引发爆炸，造成伤害和减速。阵亡时重置闪现。");
            AddDescription("冰霜 幽灵", "每若干秒，自动施放禁锢给附近的敌人们。");
            AddDescription("渴血", "获得全能吸血。");
            AddDescription("恶趣味", "定身或缚地敌方英雄时回复生命值。");
            AddDescription("重量级打击手", "你的攻击造成相当于你一定百分比最大生命值的额外物理伤害。");
            AddDescription("冰寒", "你的减速效果可降低更多移动速度。");
            AddDescription("炼狱龙魂", "你获得炼狱龙魂。");
            AddDescription("练腿日", "获得移动速度和减速抗性。");
            AddDescription("由心及物", "使你的最大生命值提升，数额相当于你一半的法务值。");
            AddDescription("海洋龙魂", "获得【海洋龙魂】。");
            AddDescription("退敌力场", "在你较低或低生命值时，附近的敌人们会被击退。");
            AddDescription("万用瞄准镜", "获得一定攻击距离(远程英雄获得的攻击距离会降低)。");
            AddDescription("自我毁灭", "每个回合开始时会有炸弹附在你身上。在若干秒后，它会爆炸，造成巨额真实伤害并击飞附近的敌人们。");
            AddDescription("暗影疾奔", "在使用一个冲刺、跳跃、闪烁或传送类技能或离开潜行状态之后，获得持续移动速度。");
            AddDescription("扇巴掌", "每当你定身或缚地敌人时，获得可叠加的适应之力。阵亡时损失层数。");
            AddDescription("天音爆", "在为你的友军提供增益效果、治疗效果或护盾效果时，会对其附近的敌人们造成真实伤害和减速。");
            AddDescription("旋转至胜", "你的旋转类技能获得技能急速并且多造成伤害！");
            AddDescription("会心防御", "你可以使用你的暴击几率来进行会心防御，使你有一定几率来使所受伤害降低。获得暴击几率。");
            AddDescription("残暴之力", "获得攻击力、技能急速和穿甲。");
            AddDescription("折磨者", "定身或缚地敌方英雄时还会施加灼烧，造成魔法伤害。灼烧可无限叠加。");
            AddDescription("质变：黄金阶", "获得1个随机黄金阶强化符文。");
            AddDescription("台风", "你的攻击会对额外目标发射弩箭，弩箭造成削减过的伤害并施加附带伤害攻击特效。");
            AddDescription("终极不可阻挡", "在你使用你的终极技能后，你获得控制免疫。");
            AddDescription("升级：收集者", "使用【收集者】的被动处决敌人时的处决阈值提升，并提供额外金币。获得250金币。");
            AddDescription("升级：弯刀", "【幽魂弯刀】和【求生索】的冷却时间降低。主动效果激活时，获得伤害增强。");
            AddDescription("升级：献祭", "【璀璨沙漏】和【日炎圣盾】每当有目标受到【献祭】效果影响时，提供金币。获得250金币。");
            AddDescription("升级：中娅", "【中娅沙漏】的冷却时间降低。你现在可以在【中娅沙漏】、【探索者的护臂】或【沃格勒特的巫师帽】主动效果持续期间移动。");
            AddDescription("良性循环", "你的治疗效果提供额外的护盾，而你的护盾提供额外的治疗效果。");
            AddDescription("巫师式思考", "获得法术强度。");
            AddDescription("魔法转物理", "将法术强度转化为额外攻击力，并获得攻击力。");
            AddDescription("坚若磐石", "每当你定身或缚地敌人时，获得护甲或者魔抗。");
            AddDescription("逃跑计划", "在低生命值时，获得巨量护盾、移动速度和缩小效果。");
            AddDescription("血上加伤", "朝被雪球标记的敌人移动时获得额外移动速度。你可以拾取友军的雪球来刷新你自己的。");
            AddDescription("闪闪现现", "获得第二个【闪现】召唤师技能和召唤师技能急速。");
            AddDescription("帽上加帽", "头戴式装备和帽子(包含帽子饮品)提供法术强度和魔法抗性。");
            AddDescription("强力护盾", "当你获得护盾时，获得适应之力。");
            AddDescription("吵闹鬼", "获得【吵闹鬼】召唤师技能。");
            AddDescription("活力再生", "你的【盈能攻击】充能数会得到全额返还，如果目标与你前一次攻击的目标不是同一个。【盈能攻击】造成额外魔法伤害。");
            AddDescription("快中求稳", "在冲刺或闪烁后，获得护盾值。");
            AddDescription("防护面纱", "获得一层法术护盾来格挡下一个敌方技能。");
            AddDescription("刃下生风", "从护甲穿透和法术穿透中获得移动速度。");
            AddDescription("点亮他们！", "每第4次攻击造成额外魔法伤害。");
            AddDescription("山脉龙魂", "你获得山脉龙魂。");
            AddDescription("家园卫士", "获得移动速度，在受到伤害后失效若干秒。");
            AddDescription("叠角龙", "在你获得一个技能的永久层数时，多获得75%。");

            // === 黄金符文描述映射 ===
            AddDescription("全心为你", "你的治疗和护盾在用在友军身上时会变强。");
            AddDescription("尖端发明家", "获得装备急速。");
            AddDescription("超强大脑", "获得护盾值。阵亡时重置。");
            AddDescription("面包和黄油", "你的Q获得技能急速。");
            AddDescription("面包和奶酪", "你的E获得技能急速。");
            AddDescription("面包和果酱", "你的W获得技能急速。");
            AddDescription("星界躯体", "获得生命值，但你造成的伤害降低。");
            AddDescription("会心治疗", "你的治疗和护盾可以暴击。获得暴击几率。");
            AddDescription("黎明使者的坚决", "在低生命时，持续回复最大生命值。");
            AddDescription("魔鬼之舞", "获得【迅捷步法】和【不灭之握】基石符文。");
            AddDescription("神圣干预", "战斗开始后，召唤护体星星缓慢的降落在你身上。在它着陆时，你和附近的友军们进入免疫伤害状态。");
            AddDescription("虚幻武器", "你的技能可施加附带伤害攻击特效。");
            AddDescription("裁决使", "对低生命值的敌人们多造成伤害。在参与击杀后重置你的基础技能。");
            AddDescription("火上浇油", "你的攻击还会灼烧敌人，造成魔法伤害。灼烧可无限叠加。");
            AddDescription("闪现向前", "你的闪现有若干层充能，拥有独立冷却时间和层数充能时间。");
            AddDescription("有始有终", "获得【先攻】和【黑暗收割】基石符文。");
            AddDescription("罪恶快感", "参与击杀后，获得移动速度和攻击速度。");
            AddDescription("圣火", "你的治疗或护盾会对附近的敌人施加无限叠加的灼烧，造成魔法伤害。");
            AddDescription("不动如山", "获得【余震】和【冰川增幅】基石符文。");
            AddDescription("关键暴击", "获得暴击几率。");
            AddDescription("杀戮时间到了", "在施放你的终极技能后，将敌人打上死亡标记。这个标记会将对其造成的伤害储存起来，然后引爆，造成已储存的伤害。");
            AddDescription("基石法师", "获得【召唤：艾黎】和【奥术彗星】基石符文。");
            AddDescription("闪电打击", "获得攻击速度。在特定攻击次数/秒时，造成额外的附带魔法伤害攻击特效。");
            AddDescription("魔法飞弹", "用技能造成伤害时，会对被命中的敌人发射魔法飞弹，造成基于飞行距离提升的真实伤害。");
            AddDescription("仆从大师", "你的召唤物获得体型提升、生命值和伤害。");
            AddDescription("回力OK镖", "每若干秒朝着附近的敌人自动施放投掷回力镖，对命中的敌人们造成自适应伤害。");
            AddDescription("狂徒豪气", "你的冲刺、跳跃、闪烁或传送类技能会为你提供护甲和魔法抗性。");
            AddDescription("溢流", "你的潜力值消耗翻倍。你的技能的治疗效果、护盾效果和伤害获得提升，提升幅度基于你的最大法力值。");
            AddDescription("坚韧", "获得基础生命回复，在低生命值时还会获得提升。");
            AddDescription("任务：钢化你心", "需求：持有【心之钢】且层数在一定层数或以上。奖励：将你的心之钢层数乘以一定倍数。");
            AddDescription("古式佳酿", "使用技能时会回复生命值。");
            AddDescription("循环往复", "获得技能急速。");
            AddDescription("无休回复", "你每移动1000码距离就会回复生命值。");
            AddDescription("更万用的瞄准镜", "获得一定攻击距离(远程英雄获得的攻击距离会降低)。");
            AddDescription("炽烈黎明", "你的技能会标记敌人，使其在被你的友军的下一个攻击或技能命中时会受到额外魔法伤害。");
            AddDescription("缩小射线", "你的攻击会对敌人造成伤害削减效果。");
            AddDescription("老练狙神", "用非终极技能命中远距离的敌人时，返还该技能冷却时间(周期性技能返还降低)。");
            AddDescription("一板一眼", "你的攻击速度变为固定数值。所有额外攻击速度会被转化为攻击力。");
            AddDescription("灵魂虹吸", "获得暴击几率和作用于暴击的生命偷取。");
            AddDescription("心灵净化", "参与击杀英雄时，在其上方产生爆炸，对敌人造成伤害。随后它会持续存在若干秒，对敌人造成减速效果。");
            AddDescription("坦克引擎", "参与击杀时，获得层数，使你的体型增大并提升最大生命值。阵亡时损失层数。");
            AddDescription("穿针引线", "获得护甲穿透和法术穿透。");
            AddDescription("质变：棱彩阶", "获得1个随机棱彩阶强化符文。");
            AddDescription("接二连三", "每第三次攻击，你的附带伤害攻击特效会触发第二次。");
            AddDescription("升级：狂妄", "参与击杀时，【狂妄】回复你的生命值，并且在增益持续时间提供移动速度，治疗量和移动速度均随层数增长。此外，获得250金币。");
            AddDescription("升级：无尽之刃", "获得暴击几率和500金币。装备【无尽之刃】后，你的暴击会随机造成额外暴击伤害。");
            AddDescription("升级：耀光", "你的【咒刃】效果造成额外目标最大生命值物理伤害并治疗你自身。此外，获得250金币。");
            AddDescription("易损", "你的装备效果和持续伤害效果可以暴击来造成额外伤害。获得暴击几率。");
            AddDescription("急急小子", "获得移动速度，相当于你的一定百分比技能急速。");
            AddDescription("作弊：我能回城！", "按下【回城】能够回到基地，购买装备并且获得强化符文。");
            AddDescription("暴击律动", "获得暴击几率。你的普攻在暴击时获得可叠加的攻击速度。");
            AddDescription("夜狩", "在对敌人造成伤害的若干秒内参与击杀该敌人会使你进入隐身状态。");
            AddDescription("升级：雪球", "你的雪球获得技能急速。击中时会产生一片区域，使其中的敌人受到减速效果，以及造成魔法伤害。");
            AddDescription("吸血习性", "你不再能够被友军治疗或获得任何生命回复。获得全能吸血。");
            AddDescription("神射法师", "你的攻击造成相当于你100%法术强的额外物理伤害。");

            // === 棱彩符文描述映射 ===
            AddDescription("回归基本功", "你的终极技能已被封印。获得技能伤害、治疗效果、护盾、技能急速。");
            AddDescription("利刃华尔兹", "让你进入不可被选取状态，在此期间对敌人进行突进并造成物理伤害。");
            AddDescription("你摸不到", "施放你的终极技能会使你进入免疫伤害状态。");
            AddDescription("死亡之环", "你造成的治疗效果和生命回复效果会对敌方英雄造成魔法伤害。");
            AddDescription("小丑学院", "获得召唤师技能【欺诈魔术】。获得【背刺】和【幻像】被动。");
            AddDescription("巨像的勇气", "定身或缚地敌方英雄后获得护盾值。");
            AddDescription("残忍", "定身或缚地敌方英雄时，召唤彗星至敌人上方。彗星着陆时造成魔法伤害。");
            AddDescription("全凭身法", "你的冲刺、跳跃、闪烁或传送类技能获得技能急速。");
            AddDescription("亮出你的剑", "变为近战状态，并获得攻击力、生命值、攻击速度、生命偷取和移动速度。");
            AddDescription("双刀流", "在攻击时，发射弩箭，造成效能削减的附带伤害攻击特效。获得攻击速度。");
            AddDescription("尤里卡", "获得相当于一定百分比法术强度的技能急速。");
            AddDescription("连拨击锤", "在你攻击时，发射额外弩箭，弩箭造成基于移动距离提升的物理伤害。弩箭能够暴击前附带攻击特效。");
            AddDescription("感受燃烧", "获得召唤师技能【感觉灼烧】。");
            AddDescription("精怪魔法", "你的终极技能的伤害会对敌人造成持续若干秒的变形效果。");
            AddDescription("巨人杀手", "体型变小，获得移动速度，对体型大于你的敌方英雄造成额外伤害。");
            AddDescription("歌利亚巨人", "体型变大，获得生命值和适应之力。");
            AddDescription("炼狱导管", "你的技能会施加无限叠加的灼烧，造成魔法伤害。你的所有灼烧效果会在对敌人们造成伤害时使你的各基础技能的冷却时间缩短。");
            AddDescription("珠光护手", "你的技能可以造成暴击。获得基础与额外暴击几率。");
            AddDescription("科学狂人", "在回合开始时，你的体型要么变大(获得适应之力和生命值)要么变小(获得技能急速和移动速度)。");
            AddDescription("物法皆修", "你的每次攻击为你提供可叠加的法术强度，并且你的每次技能为你提供可叠加的攻击力，增益可无限叠加并刷新。");
            AddDescription("秘术冲拳", "附带伤害攻击特效使你的各个基础技能的冷却时间缩减其剩余冷却时间。");
            AddDescription("全能龙魂", "获得3个随机龙魂。");
            AddDescription("量子计算", "每若干秒在你周围自动施放一次巨型斩击，造成物理伤害。被外沿命中的敌人会被施加减速，受到额外的物理伤害，并且你回复生命值。");
            AddDescription("任务：海牛阿福的勇士", "需求：参与击杀一定次数。奖励：金铲铲");
            AddDescription("任务：沃格勒特的巫师帽", "即刻：获得【无用大棒】。需求：持有【灭世者的死亡之帽】和【中娅沙漏】。奖励：获得【沃格勒特的巫师帽】。");
            AddDescription("最万用的瞄准镜", "获得一定攻击距离(远程英雄获得的攻击距离会降低)。");
            AddDescription("慢炖", "对附近的敌方英雄们施加可无限叠加的灼烧，造成魔法伤害。");
            AddDescription("战争交响乐", "获得【致命节奏】和【征服者】基石符文。");
            AddDescription("踢踏舞", "你的攻击会为你提供移动速度。获得攻击速度。增益可无限叠加并刷新。");
            AddDescription("质变：混沌", "获得2个随机的强化符文。");
            AddDescription("终极刷新", "在施放终极技能后刷新你的终极技能。");
            AddDescription("风语者的祝福", "从护甲穿透和法术穿透中获得移动速度。");
            AddDescription("小猫咪找妈妈", "站在一个友军的附近并将其标记为你的妈妈。当朝着妈妈移动时，获得移动速度。当靠近你的妈妈时，获得移动速度、治疗和护盾强度。");
            AddDescription("史上最大雪球", "你的雪球获得技能急速。你的雪球现在变得非常大，并且可以穿过小兵。它会对敌人造成减速，将他们击飞，并造成额外伤害。");
            AddDescription("至高天诺言", "传送至你的友军并在着陆时提供护盾。");
            AddDescription("最终形态", "在施放你的终极技能时，获得护盾、全能吸血和额外移动速度。");
            AddDescription("砍伤", "攻击造成附带真实伤害攻击特效。");
            AddDescription("玻璃大炮", "减少最大生命值。造成额外真实伤害。");
            AddDescription("夺金", "用攻击或技能对英雄造成伤害时会造成额外魔法伤害，并为你提供金币和移动速度。");
            AddDescription("尊我为王", "当你第一次携带一件传说级装备并乘坐敌人的传送门时，你会升级你的装备并获得随机棱彩阶强化符文。");
            AddDescription("激光治疗", "施放治疗激光，治疗友军生命值，对敌人造成魔法伤害并使敌人减速。");
            AddDescription("不祥契约", "基于已损失生命值，获得法术强度，移动速度和全能吸血。施放技能会消耗你的当前生命值。");
            AddDescription("蛋白粉奶昔", "获得治疗和护盾强度。基于额外护甲和魔法抗性。");
            AddDescription("王中王,靴中靴", "即刻：随机获得一双升级后的靴子。需求：完成其任务，以更换为另一双。奖励：嘉文一世之靴。");
            AddDescription("雪球扭蛋机", "在使用雪球冲刺后，随机对你自身施放一个增益型的召唤师技能(或对你的目标施放一个减益型的召唤师技能)。");
            AddDescription("终极唤醒", "在施放你的终极技能后，重置你所有基础技能的冷却时间。");
            AddDescription("升级：米凯尔的祝福", "【米凯尔的祝福】的冷却时间降低。获得金币。使用【米凯尔的祝福】的主动效果时，对附近的友军施放光波，清除其限制和定身效果，回复生命值，并且提供韧性。");
            AddDescription("地狱三头犬", "获得【丛刃】和【强攻】基石符文。丛刃 - 你的前3次攻击大幅提升攻击速度（近战140%，远程80%）强攻 - 对相同敌人命中3次攻击后，会造成爆发性的伤害（120-360，基于等级）并提升你的伤害，直到你离开战斗（15%）。");
            AddDescription("大地苏醒", "你的冲刺、闪烁或传送类技能会留下一条延迟爆炸的轨迹。");
            AddDescription("轨道镭射", "获得召唤师技能轨道镭射，在一阵延迟后，召唤一道轨道镭射光束落下，造成真实伤害外加持续造成魔法伤害。");
            AddDescription("潘朵拉的盒子", "将你的所有强化符文变为随机的棱彩阶强化符文。");
            AddDescription("精准奇才", "当你对700码距离以外的敌人造成伤害时，会朝该方向自动施放一道伊泽瑞尔的R，对途径的敌人们造成100-350（+80%额外攻击力）（+60%法术强度）魔法伤害。冷却时间：冷却时间15秒，并非单目标冷却。");
            return descriptionMap;
        }

        // 查找方法
        public AugmentInfo GetAugmentById(int id)
        {
            return _augments?.FirstOrDefault(a => a.Id == id);
        }

        // 获取海克斯符文信息
        public async Task<(Image image, string name, string description)> GetAugmentInfoAsync(int id)
        {
            var augment = GetAugmentById(id);
            if (augment == null)
            {
                Debug.WriteLine($"未找到海克斯符文: {id}");
                return (null, $"Augment {id}", "");
            }

            string name = augment.Name;
            string desc = augment.Description;

            // 清理描述文本：移除换行符和多余空格
            string cleanDescription = CleanDescription(desc);

            // 将稀有度信息拼接到描述中
            string rarityChinese = RarityToChinese(augment.Rarity);
            string fullDescription = $"[{rarityChinese}] {cleanDescription}";

            if (string.IsNullOrEmpty(augment.IconPath))
            {
                Debug.WriteLine($"海克斯符文图标路径为空: {id}");
                return (null, name, fullDescription);
            }

            // 使用LCU本地路径直接获取图标
            string iconPath = augment.IconPath;

            // 确保缓存目录存在
            string cacheDir = Path.Combine(Application.StartupPath, "Assets", "augments");
            Directory.CreateDirectory(cacheDir);
            string cachePath = Path.Combine(cacheDir, $"{id}.png");

            // 1. 首先尝试从本地缓存加载图片
            if (File.Exists(cachePath))
            {
                try
                {
                    using (var stream = File.OpenRead(cachePath))
                    {
                        var image = Image.FromStream(stream);
                        //Debug.WriteLine($"从缓存加载海克斯符文: {id}");
                        return ((Image)image.Clone(), name, fullDescription); // 返回包含稀有度的描述
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"缓存加载失败: {id}, {ex.Message}");
                }
            }

            // 2. 本地没有缓存，从LCU获取图片
            try
            {
                var response = await _lcuClient.GetAsync(iconPath);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"图标获取失败: {iconPath} 状态码: {response.StatusCode}");
                    return (null, name, fullDescription);
                }

                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var image = Image.FromStream(stream);

                    // 3. 将获取的图片保存到本地缓存
                    try
                    {
                        await SaveRuneImageToCacheAsync(image, cachePath);
                        Debug.WriteLine($"已缓存海克斯符文: {id}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"缓存保存失败: {id}, {ex.Message}");
                    }

                    return (image, name, fullDescription); // 返回包含稀有度的描述
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"图标加载异常: {id}, {ex.Message}");
                return (null, name, fullDescription);
            }
        }

        // 清理描述文本的方法
        private string CleanDescription(string description)
        {
            if (string.IsNullOrEmpty(description))
                return "";

            // 替换各种换行符为空格
            return description
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Trim();
        }

        // 稀有度转换为中文
        private string RarityToChinese(string rarity)
        {
            return rarity?.ToLower() switch
            {
                "ksilver" => "白银",
                "kgold" => "黄金",
                "kprismatic" => "棱彩",
                _ => "未知"
            };
        }
        #endregion
    }
}
