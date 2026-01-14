using League.Models;
using League.UIState;
using League.uitls;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO.Compression;
using static League.FormMain;

namespace League.Managers
{
    /// <summary>
    /// 管理应用程序配置、版本更新和设置
    /// </summary>
    public class ConfigUpdateManager
    {
        private readonly FormMain _mainForm;  // 新增：保存主窗体引用

        /// <summary>
        /// 增加超时时间（连接 + 整体）
        /// </summary>
        private static readonly HttpClient http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            EnableMultipleHttp2Connections = true
        })
        {
            Timeout = TimeSpan.FromSeconds(120)
        };

        private LeagueConfig? _config;

        // 修改构造函数（或加一个重载）
        public ConfigUpdateManager(FormMain mainForm)
        {
            _mainForm = mainForm;
        }

        static ConfigUpdateManager()
        {
            // GitHub 要求加 User-Agent，否则会被限流
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueHistory-AutoUpdater/1.0");
        }

        #region 版本更新管理
        /// <summary>
        /// 程序启动时调用即可（你已经在 FormMain_Load 里调用了）
        /// </summary>
        public async Task CheckForUpdates()
        {
            try
            {
                string localVerStr = VersionInfo.GetLocalVersion();
                var localVer = VersionInfo.Parse(localVerStr);

                // 调用 GitHub API 获取 latest release
                string json = await http.GetStringAsync(
                    "https://api.github.com/repos/mizi6654/LeagueHistory/releases/latest");

                var release = JObject.Parse(json);

                string tag = release["tag_name"]?.ToString() ?? "";
                var remoteVer = VersionInfo.Parse(tag);

                // 没有新版本
                if (remoteVer <= localVer)
                {
                    Debug.WriteLine("[自动更新] 当前已是最新版本：" + localVerStr);
                    return;
                }

                string changelog = release["body"]?.ToString()?.Replace("\r\n", "\n") ?? "无更新日志";
                string releaseName = release["name"]?.ToString() ?? tag;

                // 找 zip 资源（你每次只上传一个 zip，名字随意，只要后缀是 .zip）
                string zipUrl = null;
                var assets = release["assets"] as JArray;
                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        string name = asset["name"]?.ToString() ?? "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            zipUrl = asset["browser_download_url"]?.ToString();
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(zipUrl))
                {
                    MessageBox.Show("检测到新版本，但未找到 zip 下载地址，请手动更新。", "更新提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 弹窗确认
                string msg = $"发现新版本：{releaseName}\n\n" +
                             $"当前版本：{localVerStr}\n" +
                             $"更新内容：\n{changelog}\n\n" +
                             $"是否立即下载并自动更新？（更新后程序将自动重启）";

                if (MessageBox.Show(msg, "软件更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    Debug.WriteLine("[自动更新] 用户取消更新");
                    return;
                }

                // 开始更新
                await PerformAutoUpdate(zipUrl, tag.TrimStart('v', 'V'));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[自动更新检查失败] " + ex.Message);
                // 不打扰用户，只在调试输出里看得到
            }
        }
        

        private async Task PerformAutoUpdate(string zipUrl, string newVersion)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "LeagueUpdaterTemp");
            string zipPath = Path.Combine(tempDir, "update.zip");
            string extractPath = Path.Combine(tempDir, "extract");

            ProgressForm progressForm = null;

            try
            {
                progressForm = new ProgressForm();
                progressForm.Owner = _mainForm;
                progressForm.Show(_mainForm);

                // 构造函数已设置初始提示，这里只加延迟，确保用户看到至少 3 秒
                await Task.Delay(3000);  // 3 秒延迟，让初始文字充分显示

                await Task.Run(async () =>
                {
                    // 清空临时目录...
                    if (Directory.Exists(tempDir))
                    {
                        try { Directory.Delete(tempDir, true); }
                        catch { }
                    }
                    Directory.CreateDirectory(tempDir);
                    Directory.CreateDirectory(extractPath);

                    // 下载 + 重试 + 进度（保持原有）
                    int retryCount = 0;
                    const int maxDownloadRetries = 3;
                    bool downloadSuccess = false;

                    while (retryCount < maxDownloadRetries && !downloadSuccess)
                    {
                        try
                        {
                            using var response = await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead);
                            response.EnsureSuccessStatusCode();

                            var totalBytes = response.Content.Headers.ContentLength.GetValueOrDefault(-1L);
                            using var remoteStream = await response.Content.ReadAsStreamAsync();
                            using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

                            byte[] buffer = new byte[81920];
                            long downloadedBytes = 0;
                            int bytesRead;

                            while ((bytesRead = await remoteStream.ReadAsync(buffer)) > 0)
                            {
                                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                                downloadedBytes += bytesRead;

                                if (totalBytes > 0)
                                {
                                    int percent = (int)((downloadedBytes * 100L) / totalBytes);
                                    progressForm.Invoke(() => progressForm.UpdateStatus(
                                        $"从 GitHub 下载中... {percent}% ({downloadedBytes / 1024 / 1024} MB)\r\n" +
                                        "（速度受网络影响，可能较慢，请勿关闭窗口）", percent));
                                }
                            }

                            downloadSuccess = true;
                        }
                        catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
                        {
                            retryCount++;
                            if (retryCount >= maxDownloadRetries)
                            {
                                throw new Exception($"下载失败，已重试 {maxDownloadRetries} 次", ex);
                            }

                            progressForm.Invoke(() => progressForm.UpdateStatus(
                                $"下载失败，正在第 {retryCount}/{maxDownloadRetries} 次重试...\r\n" +
                                "（可能是网络波动，请保持耐心）", null));

                            await Task.Delay(3000 * retryCount);
                        }
                    }

                    if (!downloadSuccess)
                    {
                        throw new Exception("多次尝试后仍无法下载更新包");
                    }

                    progressForm.Invoke(() => progressForm.UpdateStatus("下载完成，正在解压并覆盖文件..."));

                    // 3. 解压（你的原有逻辑）
                    using (var archive = ZipFile.OpenRead(zipPath))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;

                            string destPath = Path.Combine(extractPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));

                            string destDir = Path.GetDirectoryName(destPath);
                            if (!Directory.Exists(destDir))
                                Directory.CreateDirectory(destDir);

                            if (File.Exists(destPath))
                                File.Delete(destPath);

                            entry.ExtractToFile(destPath);
                        }
                    }

                    progressForm.Invoke(() => progressForm.UpdateStatus("解压完成，正在准备重启..."));

                    // 4. 生成 bat（不变）
                    string batPath = Path.Combine(tempDir, "update.bat");
                    string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                    string exeName = Path.GetFileName(Application.ExecutablePath);

                    string batContent = $@"
                    @echo off
                    title 更新中，请勿关闭...
                    timeout /t 2 /nobreak >nul
                    xcopy ""{extractPath}\*.*"" ""{appDir}\"" /s /e /y /q /i /h
                    echo {newVersion} > ""{appDir}\version.txt""
                    start """" /d ""{appDir}"" ""{exeName}""
                    rd /s /q ""{tempDir}""
                    exit";

                    File.WriteAllText(batPath, batContent);

                    // 5. 启动 bat
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = batPath,
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });

                    // 6. 关闭进度窗并退出
                    progressForm.Invoke(() =>
                    {
                        progressForm.Complete("更新完成，正在重启程序...");
                        Application.Exit();
                    });
                });
            }
            catch (Exception ex)
            {
                // 失败处理不变...
            }
        }

        /// <summary>
        /// 处理可用更新
        /// </summary>
        private async Task HandleUpdateAvailable(VersionInfo remoteVersion, VersionInfo localVersion)
        {
            var changelogStr = string.Join("\n", remoteVersion.changelog);
            var message = BuildUpdateMessage(remoteVersion, localVersion, changelogStr);

            var result = MessageBox.Show(message, "版本更新", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

            if (result == DialogResult.OK)
            {
                await ProcessUpdate(remoteVersion);
            }
            else
            {
                Debug.WriteLine("用户取消更新");
            }
        }

        /// <summary>
        /// 构建更新消息
        /// </summary>
        private string BuildUpdateMessage(VersionInfo remoteVersion, VersionInfo localVersion, string changelog)
        {
            string localVersionStr = localVersion?.version ?? "未知";
            return $"检测到新版本 {remoteVersion.version} ({remoteVersion.date})\n\n" +
                   $"当前版本: {localVersionStr}\n\n" +
                   $"更新内容：\n{changelog}\n\n" +
                   "点击确定将打开下载页面，请手动下载解压使用。";
        }

        /// <summary>
        /// 处理更新流程
        /// </summary>
        private async Task ProcessUpdate(VersionInfo remoteVersion)
        {
            try
            {
                // 更新本地版本号文件
                UpdateLocalVersionFile(remoteVersion.version);

                // 打开下载链接
                OpenDownloadLink(remoteVersion.updateUrl);
            }
            catch (Exception ex)
            {
                ShowDownloadError(ex.Message, remoteVersion.updateUrl);
            }
        }

        /// <summary>
        /// 更新本地版本号文件
        /// </summary>
        private void UpdateLocalVersionFile(string newVersion)
        {
            string versionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
            File.WriteAllText(versionFilePath, newVersion);
            Debug.WriteLine($"已更新本地版本号: {newVersion}");
        }

        /// <summary>
        /// 打开下载链接
        /// </summary>
        private void OpenDownloadLink(string url)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            Debug.WriteLine($"已打开下载链接: {url}");
        }

        /// <summary>
        /// 显示下载错误
        /// </summary>
        private void ShowDownloadError(string errorMessage, string url)
        {
            MessageBox.Show($"打开下载链接失败: {errorMessage}\n请手动输入链接访问: {url}",
                "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// 显示更新错误
        /// </summary>
        private void ShowUpdateError(string errorMessage)
        {
            MessageBox.Show($"检查更新时发生错误: {errorMessage}",
                "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        #endregion

        #region 配置管理
        /// <summary>
        /// 加载应用程序配置
        /// </summary>
        public LeagueConfig LoadConfig()
        {
            if (_config == null)
            {
                _config = LeagueConfigManager.Load();
            }
            return _config;
        }

        /// <summary>
        /// 保存应用程序配置
        /// </summary>
        public void SaveConfig()
        {
            if (_config != null)
            {
                LeagueConfigManager.Save(_config);
            }
        }

        /// <summary>
        /// 处理筛选模式设置变更
        /// </summary>
        public void HandleFilterModeSettingChanged(bool isChecked, bool isInitializing)
        {
            if (isInitializing)
                return;

            if (_config != null)
            {
                _config.FilterByGameMode = isChecked; 
                SaveConfig();

                Debug.WriteLine($"[配置] 筛选模式设置已更新: {isChecked}");
            }
        }

        /// <summary>
        /// 处理自动退出设置变更（如果不再使用，可以删除这个方法）
        /// </summary>
        public void HandleAutoExitSettingChanged(bool isChecked, bool isInitializing)
        {
            if (isInitializing)
                return;

            if (_config != null)
            {
                _config.AutoExitGameAfterEnd = isChecked;
                SaveConfig();

                Debug.WriteLine($"[配置] 自动退出游戏设置已更新: {isChecked}");
            }
        }
        #endregion

        #region 预热功能
        /// <summary>
        /// 预热UI组件（减少首次加载延迟）
        /// </summary>
        public void PreWarmUiComponents()
        {
            PreWarmControls();
            PreWarmResourcesAsync();
        }

        /// <summary>
        /// 预热控件
        /// </summary>
        private void PreWarmControls()
        {
            try
            {
                using (var dummy = new MatchTabPageContent())
                {
                    dummy.CreateControl();
                    using (var g = dummy.CreateGraphics()) { }
                }
                Debug.WriteLine("UI 预热完成（控件实例化）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UI 预热失败: {ex}");
            }
        }

        /// <summary>
        /// 异步预热资源
        /// </summary>
        private void PreWarmResourcesAsync()
        {
            Task.Run(async () =>
            {
                try
                {
                    // 预热LCU客户端
                    await PreWarmLcuClient();

                    // 预热资源加载器
                    await PreWarmResourceLoader();

                    Debug.WriteLine("后台预热完成（异步方法 + 资源）");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"后台预热失败: {ex}");
                }
            });
        }

        /// <summary>
        /// 预热LCU客户端
        /// </summary>
        private async Task PreWarmLcuClient()
        {
            try
            {
                await Globals.lcuClient.GetSummonerByNameAsync("fake_prewarm#0000");
            }
            catch { }

            try
            {
                await Globals.lcuClient.GetCurrentRankedStatsAsync("fake-prewarm-puuid");
            }
            catch { }
        }

        /// <summary>
        /// 预热资源加载器
        /// </summary>
        private async Task PreWarmResourceLoader()
        {
            try
            {
                await Globals.resLoading.GetChampionInfoAsync(1);
                await Profileicon.GetProfileIconAsync(1);
            }
            catch { }
        }
        #endregion
    }
}