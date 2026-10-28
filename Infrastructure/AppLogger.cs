using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace League.Infrastructure
{
    public sealed class DebugFileTraceListener : TraceListener
    {
        private static readonly object _lock = new();
        private readonly string _logDir;

        public DebugFileTraceListener()
        {
            _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
            Directory.CreateDirectory(_logDir);
        }

        public override void Write(string? message) => WriteLine(message);

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrEmpty(message)) return;

            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
            string path = Path.Combine(_logDir, $"{DateTime.Now:yyyy-MM-dd}.log");

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                catch { }
            }
        }

        /// <summary>
        /// 清理超过保留天数的日志和隐藏玩家 json
        /// 建议在软件启动时后台调用一次
        /// </summary>
        /// <param name="retentionDays">保留天数，≤0 表示不清理</param>
        public static void CleanOldLogs(int retentionDays = 7)
        {
            if (retentionDays <= 0) return;

            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
                if (!Directory.Exists(logDir)) return;

                DateTime cutoff = DateTime.Today.AddDays(-retentionDays);
                string todayLogName = $"{DateTime.Today:yyyy-MM-dd}.log";

                // 匹配 2026-10-29.log
                var dailyLogRegex = new Regex(@"^(\d{4}-\d{2}-\d{2})\.log$", RegexOptions.IgnoreCase);
                // 匹配 hidden_xxx_20261029_143022.json
                var hiddenJsonRegex = new Regex(@"hidden_.*_(\d{8})_\d{6}\.json$", RegexOptions.IgnoreCase);

                foreach (string filePath in Directory.EnumerateFiles(logDir))
                {
                    string fileName = Path.GetFileName(filePath);

                    // 永远不删当天正在写的日志
                    if (string.Equals(fileName, todayLogName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    DateTime? fileDate = null;

                    // 1. 日常日志：从文件名解析 yyyy-MM-dd
                    var m1 = dailyLogRegex.Match(fileName);
                    if (m1.Success &&
                        DateTime.TryParseExact(m1.Groups[1].Value, "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
                    {
                        fileDate = d1.Date;
                    }

                    // 2. 隐藏玩家 json：从文件名解析 yyyyMMdd
                    if (fileDate == null)
                    {
                        var m2 = hiddenJsonRegex.Match(fileName);
                        if (m2.Success &&
                            DateTime.TryParseExact(m2.Groups[1].Value, "yyyyMMdd",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
                        {
                            fileDate = d2.Date;
                        }
                    }

                    // 3. 兜底：用最后写入时间
                    if (fileDate == null)
                    {
                        try
                        {
                            fileDate = File.GetLastWriteTime(filePath).Date;
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    if (fileDate < cutoff)
                    {
                        try
                        {
                            File.Delete(filePath);
                            Debug.WriteLine($"[LogCleanup] 已删除过期文件: {fileName}");
                        }
                        catch (Exception ex)
                        {
                            // 被占用就跳过，不影响启动
                            Debug.WriteLine($"[LogCleanup] 删除失败 {fileName}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LogCleanup] 清理过程异常: {ex.Message}");
            }
        }
    }
}