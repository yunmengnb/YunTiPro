using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace YunTiPro
{
    /// <summary>一个可分流的应用条目：显示名 + exe 完整路径</summary>
    public class InstalledAppInfo
    {
        public string Name = "";
        public string ExePath = "";
    }

    /// <summary>
    /// 枚举设备上可分流的应用程序，来源合并去重：
    /// (a) 开始菜单快捷方式（WScript.Shell COM 解析 .lnk 目标）；
    /// (b) 注册表 Uninstall 的 DisplayIcon / InstallLocation 启发式推导主 exe；
    /// (c) 当前运行中的进程路径。
    /// </summary>
    public static class InstalledApps
    {
        /// <summary>合并三个来源，按显示名排序返回</summary>
        public static List<InstalledAppInfo> GetApps()
        {
            var map = new Dictionary<string, InstalledAppInfo>(StringComparer.OrdinalIgnoreCase);
            CollectFromStartMenu(map);
            CollectFromRegistry(map);
            CollectFromProcesses(map);

            var list = new List<InstalledAppInfo>(map.Values);
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        // ---------- (a) 开始菜单 .lnk ----------

        private static void CollectFromStartMenu(Dictionary<string, InstalledAppInfo> map)
        {
            var dirs = new List<string>();
            try { dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")); } catch { }
            try { dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")); } catch { }

            foreach (var dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                string[] links;
                try { links = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories); }
                catch { continue; }
                foreach (var lnk in links)
                {
                    try
                    {
                        string target = ResolveShortcut(lnk);
                        if (IsUsableExe(target))
                        {
                            string name = Path.GetFileNameWithoutExtension(lnk);
                            Add(map, name, target);
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>通过 WScript.Shell COM（晚期绑定）解析快捷方式目标</summary>
        private static string ResolveShortcut(string lnkPath)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                shell = Activator.CreateInstance(t);
                shortcut = t.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                if (shortcut == null) return null;
                var target = shortcut.GetType().InvokeMember("TargetPath",
                    System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
                return target;
            }
            catch { return null; }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }

        // ---------- (b) 注册表 Uninstall ----------

        private static void CollectFromRegistry(Dictionary<string, InstalledAppInfo> map)
        {
            const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            ReadUninstallHive(map, RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), uninstall);
            ReadUninstallHive(map, RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32), uninstall);
            ReadUninstallHive(map, Registry.CurrentUser, uninstall);
        }

        private static void ReadUninstallHive(Dictionary<string, InstalledAppInfo> map, RegistryKey hive, string subKeyPath)
        {
            RegistryKey root = null;
            try
            {
                root = hive.OpenSubKey(subKeyPath, false);
                if (root == null) return;
                foreach (var name in root.GetSubKeyNames())
                {
                    try
                    {
                        using (var sub = root.OpenSubKey(name, false))
                        {
                            if (sub == null) continue;
                            var displayName = sub.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName)) continue;
                            object sysComp = sub.GetValue("SystemComponent");
                            if (sysComp is int && (int)sysComp == 1) continue;

                            // 优先 DisplayIcon 直接指向的 exe
                            var icon = sub.GetValue("DisplayIcon") as string;
                            string exe = ExtractExePath(icon);
                            if (IsUsableExe(exe))
                            {
                                Add(map, displayName.Trim(), exe);
                                continue;
                            }
                            // 退而求其次：InstallLocation 下猜主 exe
                            var loc = sub.GetValue("InstallLocation") as string;
                            exe = GuessMainExe(loc, displayName);
                            if (IsUsableExe(exe)) Add(map, displayName.Trim(), exe);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                if (root != null) root.Dispose();
            }
        }

        /// <summary>从 DisplayIcon 值提取 exe 路径（处理引号与 ",0" 图标索引后缀）</summary>
        private static string ExtractExePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string v = value.Trim();
            if (v.StartsWith("\""))
            {
                int end = v.IndexOf('"', 1);
                if (end > 1) v = v.Substring(1, end - 1);
            }
            else
            {
                int idx = v.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) v = v.Substring(0, idx + 4);
            }
            return v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? v : null;
        }

        /// <summary>在安装目录下启发式找主 exe：优先名称与软件名相近的顶层 exe，否则取体积最大的顶层 exe</summary>
        private static string GuessMainExe(string installLocation, string displayName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(installLocation)) return null;
                string dir = installLocation.Trim().Trim('"');
                if (!Directory.Exists(dir)) return null;
                var exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                if (exes.Length == 0) return null;
                if (exes.Length == 1) return exes[0];

                // 名称相似度：exe 文件名与显示名去空格后的公共前缀长度
                string dn = (displayName ?? "").Replace(" ", "").ToLowerInvariant();
                string best = null;
                int bestScore = -1;
                foreach (var exe in exes)
                {
                    string fn = Path.GetFileNameWithoutExtension(exe).Replace(" ", "").ToLowerInvariant();
                    int common = 0;
                    while (common < fn.Length && common < dn.Length && fn[common] == dn[common]) common++;
                    if (common > bestScore) { bestScore = common; best = exe; }
                }
                if (bestScore >= 3) return best;

                // 退化为体积最大者
                string biggest = null;
                long bigSize = -1;
                foreach (var exe in exes)
                {
                    long len = new FileInfo(exe).Length;
                    if (len > bigSize) { bigSize = len; biggest = exe; }
                }
                return biggest;
            }
            catch { return null; }
        }

        // ---------- (c) 运行中的进程 ----------

        private static void CollectFromProcesses(Dictionary<string, InstalledAppInfo> map)
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string path = p.MainModule != null ? p.MainModule.FileName : null;
                    if (!IsUsableExe(path)) continue;
                    string name = p.MainModule.FileVersionInfo != null
                        ? (p.MainModule.FileVersionInfo.FileDescription ?? "").Trim()
                        : "";
                    if (string.IsNullOrEmpty(name)) name = p.ProcessName;
                    Add(map, name, path);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        // ---------- 公共 ----------

        /// <summary>可用性过滤：存在的 .exe，排除 Windows 系统目录噪音</summary>
        private static bool IsUsableExe(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            try { if (!File.Exists(path)) return false; } catch { return false; }
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(winDir) &&
                path.StartsWith(winDir, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static void Add(Dictionary<string, InstalledAppInfo> map, string name, string path)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) return;
            if (map.ContainsKey(path)) return;
            map[path] = new InstalledAppInfo { Name = name.Trim(), ExePath = path };
        }
    }
}
