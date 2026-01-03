using Microsoft.Win32;
using System.Diagnostics;

namespace League.UIHelpers
{
    public class LOLHelper
    {
        /// <summary>
        /// 自动检测 LOL 登录程序路径
        /// </summary>
        /// <returns>若找到则返回 exe 完整路径，否则返回 null</returns>
        public string GetLOLLoginExePath()
        {
            // 1. 尝试从注册表读取
            string installPath = GetLOLInstallPathFromRegistry();
            if (!string.IsNullOrEmpty(installPath))
            {
                var exe = FindLauncherExe(installPath);
                if (exe != null)
                    return exe;
            }

            // 2. 如果注册表没有，就遍历常用盘符
            string[] drives = Directory.GetLogicalDrives();
            foreach (var drive in drives)
            {
                var exe = SearchLauncherExeInDrive(drive);
                if (exe != null)
                    return exe;
            }

            return null;
        }

        private string GetLOLInstallPathFromRegistry()
        {
            string[] possibleKeys =
            {
                @"SOFTWARE\WOW6432Node\Tencent\LOL",
                @"SOFTWARE\WOW6432Node\Tencent\英雄联盟"
            };

            foreach (var key in possibleKeys)
            {
                using (RegistryKey regKey = Registry.LocalMachine.OpenSubKey(key))
                {
                    if (regKey != null)
                    {
                        var value = regKey.GetValue("Path");
                        if (value != null)
                        {
                            string path = value.ToString();
                            if (Directory.Exists(path))
                                return path;
                        }
                    }
                }
            }
            return null;
        }

        private string FindLauncherExe(string installPath)
        {
            string launcherDir = Path.Combine(installPath, "Launcher");
            if (Directory.Exists(launcherDir))
            {
                string exe1 = Path.Combine(launcherDir, "startup_runner.exe");
                if (File.Exists(exe1))
                    return exe1;

                string exe2 = Path.Combine(launcherDir, "Client.exe");
                if (File.Exists(exe2))
                    return exe2;
            }
            return null;
        }

        private string SearchLauncherExeInDrive(string driveRoot)
        {
            try
            {
                // 在该盘根目录下查找名为 "英雄联盟" 的文件夹
                string programFilesX86 = Path.Combine(driveRoot, "Program Files (x86)");
                if (Directory.Exists(programFilesX86))
                {
                    var dirs = Directory.GetDirectories(programFilesX86, "*英雄联盟*");
                    foreach (var dir in dirs)
                    {
                        var exe = FindLauncherExe(dir);
                        if (exe != null)
                            return exe;
                    }
                }
                // 再试试直接在盘符根目录下
                var rootDirs = Directory.GetDirectories(driveRoot, "*英雄联盟*");
                foreach (var dir in rootDirs)
                {
                    var exe = FindLauncherExe(dir);
                    if (exe != null)
                        return exe;
                }
            }
            catch
            {
                // 忽略权限异常
            }

            return null;
        }


        /// <summary>
        /// 启动 LOL 登录程序
        /// </summary>
        /// <param name="exePath">exe 完整路径</param>
        public void StartLOLLoginProgram(string exePath)
        {
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                Process.Start(exePath);
            }
            else
            {
                Console.WriteLine("未找到 LOL 登录程序！");
            }
        }
    }
}
