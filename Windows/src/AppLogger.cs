using System;
using System.IO;
using System.Text;

namespace YunTiPro
{
    /// <summary>
    /// 应用日志：写文件(带锁) + 内存事件供 UI 展示。
    /// 严禁记录密码、UUID、完整配置等敏感内容。
    /// </summary>
    public static class AppLogger
    {
        private static readonly object LockObj = new object();
        private static string _logFile;
        public static event Action<string> OnLog;

        public static void Init(string logDir)
        {
            Directory.CreateDirectory(logDir);
            _logFile = Path.Combine(logDir, "yuntipro.log");
        }

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }
        public static void Error(string msg) { Write("ERROR", msg); }

        private static void Write(string level, string msg)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] [" + level + "] " + Sanitize(msg);
            lock (LockObj)
            {
                try
                {
                    if (_logFile != null)
                    {
                        using (var fs = new FileStream(_logFile, FileMode.Append, FileAccess.Write, FileShare.Read))
                        using (var w = new StreamWriter(fs, Encoding.UTF8))
                        {
                            w.WriteLine(line);
                        }
                    }
                }
                catch { }
            }
            var h = OnLog;
            if (h != null) h(line);
        }

        /// <summary>脱敏：过滤明显的敏感片段（双保险，调用方本就不应传入）</summary>
        private static string Sanitize(string msg)
        {
            if (msg == null) return "";
            // 移除可能出现的 UUID 样式内容
            return System.Text.RegularExpressions.Regex.Replace(
                msg,
                "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
                "***");
        }
    }
}
