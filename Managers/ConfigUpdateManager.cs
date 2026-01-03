using League.Models;
using League.uitls;
using System.Diagnostics;
using static League.FormMain;

namespace League.Managers
{
    /// <summary>
    /// 管理应用程序配置、版本更新和设置
    /// </summary>
    public class ConfigUpdateManager
    {
        private LeagueConfig? _config;

        #region 版本更新管理
        /// <summary>
        /// 检查更新
        /// </summary>
        public async Task CheckForUpdates()
        {
            try
            {
                // 读取本地版本
                var localVersion = VersionInfo.GetLocalVersion();

                // 获取远程版本  
                var remoteVersion = await VersionInfo.GetRemoteVersion();

                if (remoteVersion != null && localVersion != null)
                {
                    // 直接比较字符串版本号
                    if (remoteVersion.version != localVersion.version)
                    {
                        await HandleUpdateAvailable(remoteVersion, localVersion);
                    }
                    else
                    {
                        Debug.WriteLine("[更新检查] 当前已是最新版本");
                    }
                }
                else if (remoteVersion != null && localVersion == null)
                {
                    // 本地没有版本文件，也提示更新
                    await HandleUpdateAvailable(remoteVersion, null);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[更新检查] 异常: {ex}");
                ShowUpdateError(ex.Message);
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