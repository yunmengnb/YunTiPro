using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace YunTiPro
{
    /// <summary>
    /// 系统代理管理：修改 HKCU Internet Settings，启用前备份，停止/崩溃后恢复。
    /// 通过 wininet InternetSetOption 通知系统立即生效。
    /// </summary>
    public class SystemProxyManager
    {
        private const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private readonly string _backupFile;

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        public class ProxyBackup
        {
            public bool ProxyEnable;
            public string ProxyServer = "";
            public string ProxyOverride = "";
        }

        public SystemProxyManager(string appDataDir)
        {
            Directory.CreateDirectory(appDataDir);
            _backupFile = Path.Combine(appDataDir, "proxy-backup.txt");
        }

        /// <summary>当前系统代理是否启用</summary>
        public bool IsSystemProxyEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegPath, false))
            {
                if (key == null) return false;
                object v = key.GetValue("ProxyEnable", 0);
                return Convert.ToInt32(v) != 0;
            }
        }

        /// <summary>
        /// 启用系统代理：先备份原设置，再写入 127.0.0.1:port。
        /// </summary>
        public void Enable(int localPort)
        {
            ProxyBackup backup = ReadCurrent();
            SaveBackup(backup);

            using (var key = Registry.CurrentUser.OpenSubKey(RegPath, true))
            {
                if (key == null) throw new InvalidOperationException("无法打开代理注册表项");
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", "127.0.0.1:" + localPort, RegistryValueKind.String);
                // 本地地址不走代理
                key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>", RegistryValueKind.String);
            }
            NotifySystem();
        }

        /// <summary>恢复启用前的代理设置</summary>
        public void Restore()
        {
            ProxyBackup backup = LoadBackup();
            using (var key = Registry.CurrentUser.OpenSubKey(RegPath, true))
            {
                if (key == null) return;
                if (backup != null)
                {
                    key.SetValue("ProxyEnable", backup.ProxyEnable ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ProxyServer", backup.ProxyServer ?? "", RegistryValueKind.String);
                    key.SetValue("ProxyOverride", backup.ProxyOverride ?? "", RegistryValueKind.String);
                }
                else
                {
                    // 无备份时保守地关闭代理，避免残留
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                }
            }
            try { if (File.Exists(_backupFile)) File.Delete(_backupFile); } catch { }
            NotifySystem();
        }

        /// <summary>
        /// 异常退出恢复：若上次启用后未正常恢复（备份文件仍存在），则还原。
        /// 返回是否执行了恢复。
        /// </summary>
        public bool RecoverIfNeeded()
        {
            if (!File.Exists(_backupFile)) return false;
            // 备份文件存在且当前代理指向本工具端口范围，视为上次未正常退出
            try { Restore(); return true; } catch { return false; }
        }

        private ProxyBackup ReadCurrent()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegPath, false))
            {
                var b = new ProxyBackup();
                if (key == null) return b;
                b.ProxyEnable = Convert.ToInt32(key.GetValue("ProxyEnable", 0)) != 0;
                b.ProxyServer = Convert.ToString(key.GetValue("ProxyServer", ""));
                b.ProxyOverride = Convert.ToString(key.GetValue("ProxyOverride", ""));
                return b;
            }
        }

        private void SaveBackup(ProxyBackup b)
        {
            // 备份内容不含敏感信息，可明文保存
            string text = (b.ProxyEnable ? "1" : "0") + "\n" + (b.ProxyServer ?? "") + "\n" + (b.ProxyOverride ?? "");
            File.WriteAllText(_backupFile, text, Encoding.UTF8);
        }

        private ProxyBackup LoadBackup()
        {
            try
            {
                if (!File.Exists(_backupFile)) return null;
                var lines = File.ReadAllLines(_backupFile, Encoding.UTF8);
                var b = new ProxyBackup();
                if (lines.Length > 0) b.ProxyEnable = lines[0] == "1";
                if (lines.Length > 1) b.ProxyServer = lines[1];
                if (lines.Length > 2) b.ProxyOverride = lines[2];
                return b;
            }
            catch { return null; }
        }

        private static void NotifySystem()
        {
            // 通知所有进程代理设置已变更并立即刷新
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
    }
}
