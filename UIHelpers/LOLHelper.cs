using Microsoft.Win32;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;

namespace League.UIHelpers
{
    public class LOLHelper
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LOLPathConfig.json");

        public void SaveCustomPath(string exePath)
        {
            try
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(new { LastLOLPath = exePath }, Formatting.Indented));
            }
            catch { }
        }

        private string LoadCustomPath()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var config = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(ConfigPath));
                    return config?.LastLOLPath?.ToString();
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 获取官方客户端启动路径（Launcher / TCLS）
        /// </summary>
        public string GetOfficialLauncherPath()
        {
            string custom = LoadCustomPath();
            if (!string.IsNullOrEmpty(custom) && File.Exists(custom))
                return custom;

            return SearchFixedSubDirs(new[] { "Launcher", "TCLS" });
        }

        /// <summary>
        /// 获取 WeGame 启动路径
        /// </summary>
        public string GetWeGameLauncherPath()
        {
            return SearchFixedSubDirs(new[] { "WeGameLauncher" });
        }

        /// <summary>
        /// 全盘搜索固定子目录
        /// </summary>
        private string SearchFixedSubDirs(string[] targetSubDirs)
        {
            try
            {
                string[] drives = Directory.GetLogicalDrives();

                foreach (var drive in drives)
                {
                    try
                    {
                        var result = SearchDirectory(drive, targetSubDirs, maxDepth: 4); // 限制深度，避免太慢
                        if (!string.IsNullOrEmpty(result))
                            return result;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        //private string SearchDirectory(string currentDir, string[] targetSubDirs, int maxDepth, int currentDepth = 0)
        //{
        //    if (currentDepth > maxDepth) return null;

        //    try
        //    {
        //        // 检查当前目录下是否有目标子目录
        //        foreach (var subDirName in targetSubDirs)
        //        {
        //            string subPath = Path.Combine(currentDir, subDirName);
        //            if (Directory.Exists(subPath))
        //            {
        //                string exe = FindExeInSubDir(subPath, subDirName);
        //                if (!string.IsNullOrEmpty(exe))
        //                    return exe;
        //            }
        //        }

        //        // 继续搜索下一级目录
        //        foreach (var dir in Directory.GetDirectories(currentDir))
        //        {
        //            // 跳过常见大目录，加快搜索
        //            string dirName = Path.GetFileName(dir)?.ToLower() ?? "";
        //            if (dirName.Contains("windows") || dirName.Contains("appdata") || dirName.Contains("$"))
        //                continue;

        //            string found = SearchDirectory(dir, targetSubDirs, maxDepth, currentDepth + 1);
        //            if (!string.IsNullOrEmpty(found))
        //                return found;
        //        }
        //    }
        //    catch { }

        //    return null;
        //}
        private string SearchDirectory(string currentDir, string[] targetSubDirs, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth > maxDepth) return null;

            string dirName = Path.GetFileName(currentDir)?.ToLowerInvariant() ?? "";

            // 加强黑名单，防止进入受保护目录
            if (dirName is "windows" or "appdata" or "programdata" or
                         "documents and settings" or "$recycle.bin" or
                         "system volume information" or "recovery" or "perflogs")
            {
                return null;
            }

            try
            {
                // 检查当前目录下是否有目标子文件夹（如 bin、x64、Game 等）
                foreach (var subDirName in targetSubDirs)
                {
                    string subPath = Path.Combine(currentDir, subDirName);
                    if (Directory.Exists(subPath))
                    {
                        string exe = FindExeInSubDir(subPath, subDirName);
                        if (!string.IsNullOrEmpty(exe))
                            return exe;
                    }
                }

                // 递归子目录
                foreach (var dir in Directory.EnumerateDirectories(currentDir))
                {
                    string found = SearchDirectory(dir, targetSubDirs, maxDepth, currentDepth + 1);
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 权限不足，安静跳过
            }
            catch (Exception ex) when (ex is IOException or PathTooLongException)
            {
                // 其他IO问题也跳过
            }

            return null;
        }

        private string FindExeInSubDir(string subDir, string subDirName)
        {
            if (subDirName == "WeGameLauncher")
            {
                string exe = Path.Combine(subDir, "launcher.exe");
                return File.Exists(exe) ? exe : null;
            }

            // Launcher 和 TCLS
            string[] candidates = subDirName == "TCLS"
                ? new[] { "client.exe" }
                : new[] { "startup_runner.exe", "Client.exe" };

            foreach (var exeName in candidates)
            {
                string fullPath = Path.Combine(subDir, exeName);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return null;
        }

        private string GetFromRegistry()
        {
            // 注册表作为最后兜底
            string[] keys =
            {
                @"SOFTWARE\WOW6432Node\Tencent\LOL",
                @"SOFTWARE\WOW6432Node\Tencent\英雄联盟"
            };

            foreach (var key in keys)
            {
                using var reg = Registry.LocalMachine.OpenSubKey(key);
                if (reg != null)
                {
                    var path = reg.GetValue("Path")?.ToString() ?? reg.GetValue("InstallPath")?.ToString();
                    if (!string.IsNullOrEmpty(path))
                    {
                        string exe = FindExeInSubDir(Path.Combine(path, "Launcher"), "Launcher");
                        if (!string.IsNullOrEmpty(exe)) return exe;
                    }
                }
            }
            return null;
        }

        public void StartOfficialClient() => StartProcess(GetOfficialLauncherPath());
        public void StartWeGameClient() => StartProcess(GetWeGameLauncherPath());

        private void StartProcess(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                MessageBox.Show("未找到启动程序，请检查游戏目录或手动启动。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show("启动失败，请手动启动客户端。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}