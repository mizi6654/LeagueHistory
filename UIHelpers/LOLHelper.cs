using Microsoft.Win32;
using Newtonsoft.Json;
using System.Diagnostics;

namespace League.UIHelpers
{
    public class LOLHelper
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LOLPathConfig.json");

        private class LOLPathConfig
        {
            public string OfficialPath { get; set; }   // 默认启动路径（优先 TCLS\client.exe）
            public string WeGamePath { get; set; }     // WeGame 启动路径
        }

        #region 配置读写

        private LOLPathConfig LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonConvert.DeserializeObject<LOLPathConfig>(json) ?? new LOLPathConfig();
                }
            }
            catch { }

            return new LOLPathConfig();
        }

        private void SaveConfig(LOLPathConfig config)
        {
            try
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
            catch { }
        }

        /// <summary>
        /// 保存官方默认启动路径
        /// </summary>
        public void SaveOfficialPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var config = LoadConfig();
            config.OfficialPath = path;
            SaveConfig(config);
        }

        /// <summary>
        /// 保存 WeGame 启动路径
        /// </summary>
        public void SaveWeGamePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var config = LoadConfig();
            config.WeGamePath = path;
            SaveConfig(config);
        }

        /// <summary>
        /// 同时保存两个路径
        /// </summary>
        public void SaveCustomPath(string officialPath, string weGamePath = null)
        {
            var config = LoadConfig();

            if (!string.IsNullOrEmpty(officialPath))
                config.OfficialPath = officialPath;

            if (!string.IsNullOrEmpty(weGamePath))
                config.WeGamePath = weGamePath;

            SaveConfig(config);
        }

        #endregion

        #region 对外接口

        /// <summary>
        /// 获取官方默认启动路径（优先 TCLS\client.exe）
        /// </summary>
        public string GetOfficialLauncherPath()
        {
            var config = LoadConfig();

            // 1. 优先使用已保存且仍然有效的路径
            if (!string.IsNullOrEmpty(config.OfficialPath) &&
                File.Exists(config.OfficialPath) &&
                IsValidLOLLauncher(config.OfficialPath))
            {
                return config.OfficialPath;
            }

            // 2. 重新搜索
            string found = SearchOfficialPath();
            if (!string.IsNullOrEmpty(found))
            {
                // 只有真正找到官方路径才覆盖
                config.OfficialPath = found;
                SaveConfig(config);
                return found;
            }

            return null;
        }

        /// <summary>
        /// 获取 WeGame 启动路径
        /// </summary>
        public string GetWeGameLauncherPath()
        {
            var config = LoadConfig();

            if (!string.IsNullOrEmpty(config.WeGamePath) &&
                File.Exists(config.WeGamePath) &&
                IsValidLOLLauncher(config.WeGamePath))
            {
                return config.WeGamePath;
            }

            string found = SearchWeGamePath();
            if (!string.IsNullOrEmpty(found))
            {
                config.WeGamePath = found;
                SaveConfig(config);
                return found;
            }

            return null;
        }

        public void StartOfficialClient() => StartProcess(GetOfficialLauncherPath());
        public void StartWeGameClient() => StartProcess(GetWeGameLauncherPath());

        #endregion

        #region 搜索逻辑

        private string SearchOfficialPath()
        {
            // 严格优先 TCLS
            string tcls = SearchFixedSubDirs(new[] { "TCLS" });
            if (!string.IsNullOrEmpty(tcls))
                return tcls;

            // 其次 Launcher
            string launcher = SearchFixedSubDirs(new[] { "Launcher" });
            if (!string.IsNullOrEmpty(launcher))
                return launcher;

            // 注册表兜底
            return GetFromRegistry();
        }

        private string SearchWeGamePath()
        {
            return SearchFixedSubDirs(new[] { "WeGameLauncher" });
        }

        private string SearchFixedSubDirs(string[] targetSubDirs)
        {
            try
            {
                foreach (var drive in Directory.GetLogicalDrives())
                {
                    try
                    {
                        string result = SearchDirectory(drive, targetSubDirs, maxDepth: 5);
                        if (!string.IsNullOrEmpty(result))
                            return result;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private string SearchDirectory(string currentDir, string[] targetSubDirs, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth > maxDepth)
                return null;

            string dirName = Path.GetFileName(currentDir)?.ToLowerInvariant() ?? "";

            // 系统目录黑名单
            if (dirName is "windows" or "appdata" or "programdata" or
                          "documents and settings" or "$recycle.bin" or
                          "system volume information" or "recovery" or "perflogs" or
                          "program files" or "program files (x86)")
            {
                return null;
            }

            try
            {
                foreach (var subDirName in targetSubDirs)
                {
                    string subPath = Path.Combine(currentDir, subDirName);
                    if (Directory.Exists(subPath))
                    {
                        string exe = FindExeInSubDir(subPath, subDirName);
                        if (!string.IsNullOrEmpty(exe) && IsValidLOLLauncher(exe))
                            return exe;
                    }
                }

                foreach (var dir in Directory.EnumerateDirectories(currentDir))
                {
                    string found = SearchDirectory(dir, targetSubDirs, maxDepth, currentDepth + 1);
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }
            catch (IOException) { }

            return null;
        }

        private string FindExeInSubDir(string subDir, string subDirName)
        {
            if (subDirName.Equals("WeGameLauncher", StringComparison.OrdinalIgnoreCase))
            {
                string exe = Path.Combine(subDir, "launcher.exe");
                return File.Exists(exe) ? exe : null;
            }

            if (subDirName.Equals("TCLS", StringComparison.OrdinalIgnoreCase))
            {
                string exe = Path.Combine(subDir, "client.exe");
                return File.Exists(exe) ? exe : null;
            }

            // Launcher
            string[] candidates = { "startup_runner.exe", "Client.exe" };
            foreach (var name in candidates)
            {
                string fullPath = Path.Combine(subDir, name);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return null;
        }

        #endregion

        #region 特征验证（已修复 client.exe 冲突问题）

        private bool IsValidLOLLauncher(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            string dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir))
                return false;

            string fileName = Path.GetFileName(exePath).ToLowerInvariant();
            string parentDirName = Path.GetFileName(dir)?.ToLowerInvariant() ?? "";

            // ========== 1. TCLS 目录 ==========
            if (parentDirName == "tcls" && fileName == "client.exe")
            {
                // 最强特征
                if (File.Exists(Path.Combine(dir, "config", "dirserver_lcu.xml")))
                    return true;

                // 备选特征
                bool hasMmog = File.Exists(Path.Combine(dir, "mmog_data.xml"));
                bool hasWeGameIni = File.Exists(Path.Combine(dir, "wegame_launch.ini"));
                return hasMmog && hasWeGameIni;
            }

            // ========== 2. Launcher 目录 ==========
            if (parentDirName == "launcher" && (fileName == "client.exe" || fileName == "startup_runner.exe"))
            {
                bool hasWGLogin = File.Exists(Path.Combine(dir, "WGLogin.dll"));
                bool hasRail = File.Exists(Path.Combine(dir, "rail_sdk_platform.dll")) ||
                               File.Exists(Path.Combine(dir, "railtr.dll"));
                bool hasProtocol = File.Exists(Path.Combine(dir, "protocol_cs.tdr")) ||
                                   File.Exists(Path.Combine(dir, "protocol_as_a6.tdr"));

                bool hasEnterLol = File.Exists(Path.Combine(dir, "data", "client_ui", "web", "assets", "enterLol.mp3"));
                bool hasLolUi = Directory.Exists(Path.Combine(dir, "tpf_ui", "res", "lol"));

                return (hasWGLogin && hasRail && hasProtocol) || hasEnterLol || hasLolUi;
            }

            // ========== 3. WeGameLauncher 目录 ==========
            if (parentDirName == "wegamelauncher" && fileName == "launcher.exe")
            {
                bool hasHealthDll = File.Exists(Path.Combine(dir, "wegame_health.dll")) ||
                                    File.Exists(Path.Combine(dir, "platform_health.dll"));

                bool hasConfig = File.Exists(Path.Combine(dir, "config", "Launcher.json")) ||
                                 File.Exists(Path.Combine(dir, "config", "Game.json")) ||
                                 File.Exists(Path.Combine(dir, "config", "Launcher_remote.json"));

                bool hasGameRes = File.Exists(Path.Combine(dir, "game", "game.ico")) ||
                                  File.Exists(Path.Combine(dir, "game", "game.png")) ||
                                  File.Exists(Path.Combine(dir, "game", "login_logo.png"));

                return hasHealthDll || (hasConfig && hasGameRes);
            }

            return false;
        }

        #endregion

        #region 注册表兜底

        private string GetFromRegistry()
        {
            string[] keys =
            {
                @"SOFTWARE\WOW6432Node\Tencent\LOL",
                @"SOFTWARE\WOW6432Node\Tencent\英雄联盟",
                @"SOFTWARE\Tencent\LOL",
                @"SOFTWARE\Tencent\英雄联盟"
            };

            foreach (var key in keys)
            {
                try
                {
                    using var reg = Registry.LocalMachine.OpenSubKey(key);
                    if (reg == null) continue;

                    var path = reg.GetValue("Path")?.ToString() ??
                               reg.GetValue("InstallPath")?.ToString();

                    if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                        continue;

                    // 优先 TCLS
                    string tclsExe = Path.Combine(path, "TCLS", "client.exe");
                    if (File.Exists(tclsExe) && IsValidLOLLauncher(tclsExe))
                        return tclsExe;

                    // 其次 Launcher
                    string clientExe = Path.Combine(path, "Launcher", "Client.exe");
                    if (File.Exists(clientExe) && IsValidLOLLauncher(clientExe))
                        return clientExe;

                    string startupExe = Path.Combine(path, "Launcher", "startup_runner.exe");
                    if (File.Exists(startupExe) && IsValidLOLLauncher(startupExe))
                        return startupExe;
                }
                catch { }
            }

            return null;
        }

        #endregion

        #region 启动进程

        private void StartProcess(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                MessageBox.Show("未找到启动程序，请检查游戏是否正确安装。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show("启动失败，请尝试手动启动客户端。", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion
    }
}