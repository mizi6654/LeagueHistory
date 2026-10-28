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
            public string OfficialPath { get; set; }   // 默认启动路径（优先 Launcher\startup_runner.exe）
            public string WeGamePath { get; set; }     // WeGame 启动路径
            public string InternationalPath { get; set; } // 外服 RiotClientServices.exe 路径
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

        public void SaveOfficialPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var config = LoadConfig();
            config.OfficialPath = path;
            SaveConfig(config);
        }

        public void SaveWeGamePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var config = LoadConfig();
            config.WeGamePath = path;
            SaveConfig(config);
        }

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
        /// 获取官方默认启动路径。
        /// 优先级：Launcher\startup_runner.exe → Launcher\Client.exe → TCLS\client.exe
        /// 每次都会尝试把缓存升级到更高优先级；找不到更优路径时才沿用旧缓存。
        /// </summary>
        public string GetOfficialLauncherPath()
        {
            var config = LoadConfig();

            // 1. 若已有缓存路径，先在同一安装目录下尝试「升级」到更优先的启动器
            if (!string.IsNullOrEmpty(config.OfficialPath) && File.Exists(config.OfficialPath))
            {
                string upgraded = TryUpgradeOfficialPath(config.OfficialPath);
                if (!string.IsNullOrEmpty(upgraded) &&
                    !string.Equals(upgraded, config.OfficialPath, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[LOLHelper] 启动路径已升级: {config.OfficialPath} → {upgraded}");
                    config.OfficialPath = upgraded;
                    SaveConfig(config);
                    return upgraded;
                }

                if (IsValidLOLLauncher(config.OfficialPath))
                    return config.OfficialPath;
            }

            // 2. 无有效缓存 → 全盘/注册表搜索
            string found = SearchOfficialPath();
            if (!string.IsNullOrEmpty(found))
            {
                config.OfficialPath = found;
                SaveConfig(config);
                Debug.WriteLine($"[LOLHelper] 搜索到官方启动路径并写入配置: {found}");
                return found;
            }

            return null;
        }

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

        /// <summary>
        /// 获取外服启动路径（RiotClientServices.exe）
        /// </summary>
        public string GetInternationalLauncherPath()
        {
            var config = LoadConfig();

            // 1. 缓存有效则直接返回
            if (!string.IsNullOrEmpty(config.InternationalPath) &&
                File.Exists(config.InternationalPath) &&
                IsValidInternationalLauncher(config.InternationalPath))
            {
                return config.InternationalPath;
            }

            // 2. 重新扫描
            string found = SearchInternationalPath();
            if (!string.IsNullOrEmpty(found))
            {
                config.InternationalPath = found;
                SaveConfig(config);
                Debug.WriteLine($"[LOLHelper] 搜索到外服启动路径并写入配置: {found}");
                return found;
            }

            return null;
        }

        public void StartInternationalClient()
        {
            string path = GetInternationalLauncherPath();
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--launch-product=league_of_legends --launch-patchline=live",
                    WorkingDirectory = Path.GetDirectoryName(path) ?? "",
                    UseShellExecute = true
                };
                Process.Start(psi);
                Debug.WriteLine($"[LOLHelper] 已启动外服: {path}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOLHelper] 启动外服异常: {ex.Message}");
            }
        }

        public void SaveInternationalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var config = LoadConfig();
            config.InternationalPath = path;
            SaveConfig(config);
        }

        /// <summary>
        /// 强制重新搜索官方路径并覆盖配置（可供设置页「重新检测」按钮调用）
        /// </summary>
        public string ForceRefreshOfficialPath()
        {
            string found = SearchOfficialPath();
            if (!string.IsNullOrEmpty(found))
            {
                var config = LoadConfig();
                config.OfficialPath = found;
                SaveConfig(config);
                Debug.WriteLine($"[LOLHelper] ForceRefresh → {found}");
                return found;
            }
            return null;
        }

        #endregion

        #region 路径升级（同安装根目录内）

        private static string GetInstallRoot(string exePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(dir)) return null;

                string parentName = Path.GetFileName(dir)?.ToLowerInvariant() ?? "";
                if (parentName is "tcls" or "launcher" or "wegamelauncher")
                    return Path.GetDirectoryName(dir);

                string grand = Path.GetDirectoryName(dir);
                if (!string.IsNullOrEmpty(grand))
                {
                    if (Directory.Exists(Path.Combine(grand, "Launcher")) ||
                        Directory.Exists(Path.Combine(grand, "TCLS")))
                        return grand;
                }

                return dir;
            }
            catch
            {
                return null;
            }
        }

        private string TryUpgradeOfficialPath(string currentPath)
        {
            string root = GetInstallRoot(currentPath);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return IsValidLOLLauncher(currentPath) ? currentPath : null;

            string[] preferred =
            {
                Path.Combine(root, "Launcher", "startup_runner.exe"),
                Path.Combine(root, "Launcher", "Client.exe"),
                Path.Combine(root, "TCLS", "client.exe"),
            };

            foreach (var candidate in preferred)
            {
                if (File.Exists(candidate) && IsValidLOLLauncher(candidate))
                    return candidate;
            }

            return IsValidLOLLauncher(currentPath) ? currentPath : null;
        }

        #endregion

        #region 搜索逻辑

        private string SearchOfficialPath()
        {
            // ★ 严格优先 Launcher（干净启动端）
            string launcher = SearchFixedSubDirs(new[] { "Launcher" });
            if (!string.IsNullOrEmpty(launcher))
                return launcher;

            string tcls = SearchFixedSubDirs(new[] { "TCLS" });
            if (!string.IsNullOrEmpty(tcls))
                return tcls;

            return GetFromRegistry();
        }

        private string SearchWeGamePath()
        {
            return SearchFixedSubDirs(new[] { "WeGameLauncher" });
        }

        private string SearchInternationalPath()
        {
            try
            {
                foreach (var drive in Directory.GetLogicalDrives())
                {
                    try
                    {
                        string[] candidates =
                        {
                            Path.Combine(drive, "Riot Games", "Riot Client", "RiotClientServices.exe"),
                            Path.Combine(drive, "Games", "Riot Games", "Riot Client", "RiotClientServices.exe"),
                            Path.Combine(drive, "Program Files", "Riot Games", "Riot Client", "RiotClientServices.exe"),
                            Path.Combine(drive, "Program Files (x86)", "Riot Games", "Riot Client", "RiotClientServices.exe"),
                        };

                        foreach (var candidate in candidates)
                        {
                            if (File.Exists(candidate) && IsValidInternationalLauncher(candidate))
                                return candidate;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return null;
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

            if (dirName is "windows" or "appdata" or "programdata" or
                          "documents and settings" or "$recycle.bin" or
                          "system volume information" or "recovery" or "perflogs" or
                          "program files" or "program files (x86)" or
                          "msocache" or "intel" or "amd" or "nvidia" or
                          "config.msi")
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

                // ★ 关键：忽略无权限目录，不再抛 UnauthorizedAccessException
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false
                };

                foreach (var dir in Directory.EnumerateDirectories(currentDir, "*", options))
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

            // Launcher：优先 startup_runner.exe
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

        #region 特征验证

        private bool IsValidLOLLauncher(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            string dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir))
                return false;

            string fileName = Path.GetFileName(exePath).ToLowerInvariant();
            string parentDirName = Path.GetFileName(dir)?.ToLowerInvariant() ?? "";

            if (parentDirName == "tcls" && fileName == "client.exe")
            {
                if (File.Exists(Path.Combine(dir, "config", "dirserver_lcu.xml")))
                    return true;

                bool hasMmog = File.Exists(Path.Combine(dir, "mmog_data.xml"));
                bool hasWeGameIni = File.Exists(Path.Combine(dir, "wegame_launch.ini"));
                return hasMmog && hasWeGameIni;
            }

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

        /// <summary>
        /// 验证是否是有效的外服 Riot Client 启动文件
        /// （根据你提供的目录结构：必须有 RiotClientServices.exe + system.yaml）
        /// </summary>
        private bool IsValidInternationalLauncher(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            if (!Path.GetFileName(exePath).Equals("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase))
                return false;

            string dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return false;

            // 核心特征：system.yaml 必须存在
            if (!File.Exists(Path.Combine(dir, "system.yaml")))
                return false;

            // 结构辅助验证（提高准确性）
            bool hasElectron = Directory.Exists(Path.Combine(dir, "RiotClientElectron"))
                               && File.Exists(Path.Combine(dir, "RiotClientElectron", "Riot Client.exe"));

            bool hasDll = File.Exists(Path.Combine(dir, "RiotClientFoundation.dll"))
                          || File.Exists(Path.Combine(dir, "RiotGamesApi.dll"));

            return hasElectron || hasDll;
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

                    string startupExe = Path.Combine(path, "Launcher", "startup_runner.exe");
                    if (File.Exists(startupExe) && IsValidLOLLauncher(startupExe))
                        return startupExe;

                    string clientExe = Path.Combine(path, "Launcher", "Client.exe");
                    if (File.Exists(clientExe) && IsValidLOLLauncher(clientExe))
                        return clientExe;

                    string tclsExe = Path.Combine(path, "TCLS", "client.exe");
                    if (File.Exists(tclsExe) && IsValidLOLLauncher(tclsExe))
                        return tclsExe;
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
                Debug.WriteLine($"[LOLHelper] 启动失败，路径无效: {exePath}");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                    UseShellExecute = true
                };
                Process.Start(psi);
                Debug.WriteLine($"[LOLHelper] 已启动: {exePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOLHelper] 启动异常: {ex.Message}");
            }
        }

        #endregion
    }
}