using Newtonsoft.Json;
using System.Diagnostics;

namespace League.model
{
    
    public class ResourceLoading
    {
        public HttpClient _lcuClient;

        public List<Champion> Champions = new List<Champion>(); //存储英雄名称
        public List<Item> Items = new List<Item>(); //存储装备名称
        public List<SummonerSpell> SummonerSpells = new List<SummonerSpell>();  //存储召唤师名称
        public List<RuneInfo> Runes = new List<RuneInfo>(); //存储符文技能名称
        public List<ProfileIcon> ProfileIcons = new List<ProfileIcon>();   //存储玩家头像信息

        public async void loadingResource(LcuSession _lcu) 
        {
            _lcuClient = _lcu.Client; // 构造函数中可以安全引用

            await LoadChampionsAsync();
            await LoadItemsAsync();
            await LoadSpellsAsync();
            await LoadRunesAsync();
        }

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
                Debug.WriteLine($"未找到ID为 {champId} 的英雄");
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
    }
}
