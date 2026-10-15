using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace YunTiPro
{
    /// <summary>应用设置（不含敏感信息，明文存储）</summary>
    public class AppSettings
    {
        public bool AutoStart;
        public bool DisclaimerAccepted;
        /// <summary>代理模式：system（系统代理）/ tun（TUN 全局接管）</summary>
        public string ProxyMode = "system";
        /// <summary>应用分流模式：off / whitelist / blacklist（仅 TUN 模式生效）</summary>
        public string AppFilterMode = "off";
        /// <summary>分流名单：分号分隔的 exe 完整路径</summary>
        public string AppFilterList = "";
    }

    /// <summary>
    /// 应用服务层：协调节点存储、核心进程、系统代理、远端验证。
    /// UI 只调用本类，不直接操作底层。
    /// </summary>
    public class AppServices
    {
        public const string Version = "1.1.3";

        private readonly string _appDir;
        private readonly string _dataDir;
        private readonly CredentialStore _store;
        private readonly XrayCoreManager _core;
        private readonly TunCoreManager _tun;
        private readonly SystemProxyManager _proxy;

        public List<NodeConfig> Nodes;
        public AppSettings Settings = new AppSettings();
        public string SelectedNodeId;

        // 当前会话的本地代理参数（随机生成）
        private int _socksPort;
        private int _httpPort;
        private string _authUser;
        private string _authPass;
        private bool _proxyActive;

        public AppServices(string appDir)
        {
            _appDir = appDir;
            _dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YunTiPro");
            Directory.CreateDirectory(_dataDir);

            AppLogger.Init(Path.Combine(_dataDir, "logs"));
            _store = new CredentialStore(Path.Combine(_dataDir, "nodes.dat"));
            string coreDir = ResolveCoreDir(appDir);
            _core = new XrayCoreManager(coreDir);
            _tun = new TunCoreManager(coreDir);
            _proxy = new SystemProxyManager(_dataDir);

            // 崩溃恢复：若上次未正常恢复系统代理，现在恢复
            if (_proxy.RecoverIfNeeded())
                AppLogger.Warn("检测到上次异常退出，已恢复系统代理设置");

            Nodes = _store.Load();
            LoadSettings();

            _core.OnOutput += line =>
            {
                // 仅记录错误与警告概要，不记录完整配置与凭据
                if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                    AppLogger.Error("xray: " + TrimLine(line));
                else if (line.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0)
                    AppLogger.Warn("xray: " + TrimLine(line));
            };

            _tun.OnOutput += line =>
            {
                if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0)
                    AppLogger.Error("sing-box: " + TrimLine(line));
                else if (line.IndexOf("warn", StringComparison.OrdinalIgnoreCase) >= 0)
                    AppLogger.Warn("sing-box: " + TrimLine(line));
            };
        }

        /// <summary>定位核心目录：优先 exe 旁的 core\，其次上一级目录的 core\（项目开发布局）</summary>
        private static string ResolveCoreDir(string appDir)
        {
            string beside = Path.Combine(appDir, "core");
            if (File.Exists(Path.Combine(beside, "xray.exe"))) return beside;
            string parent = Path.Combine(Directory.GetParent(appDir).FullName, "core");
            if (File.Exists(Path.Combine(parent, "xray.exe"))) return parent;
            return beside;
        }

        private static string TrimLine(string s)
        {
            return s.Length > 300 ? s.Substring(0, 300) : s;
        }

        // ---------- 节点管理 ----------

        public void UpsertNode(NodeConfig node)
        {
            var existing = Nodes.FirstOrDefault(n => n.Id == node.Id);
            if (existing != null)
            {
                int idx = Nodes.IndexOf(existing);
                Nodes[idx] = node;
            }
            else
            {
                Nodes.Add(node);
            }
            SaveNodes();
            AppLogger.Info("保存节点: " + node.Name + " (" + node.Protocol + ")");
        }

        public void DeleteNode(NodeConfig node)
        {
            Nodes.RemoveAll(n => n.Id == node.Id);
            if (SelectedNodeId == node.Id) SelectedNodeId = null;
            SaveNodes();
            AppLogger.Info("删除节点: " + node.Name);
        }

        public void SaveNodes()
        {
            _store.Save(Nodes);
        }

        // ---------- 代理生命周期 ----------

        /// <summary>
        /// 启动代理：随机端口 + 随机认证 → 生成配置 → 启动核心 → 设置系统代理。
        /// 返回本地 SOCKS 端口。
        /// </summary>
        public int StartProxy(NodeConfig node)
        {
            if (!node.IsOutboundCapable)
                throw new InvalidOperationException("该节点协议不能作为出口");

            if (string.Equals(Settings.ProxyMode, "tun", StringComparison.OrdinalIgnoreCase))
                return StartTun(node);
            return StartSystemProxy(node);
        }

        /// <summary>系统代理模式：xray 本地入站 + 系统代理设置</summary>
        private int StartSystemProxy(NodeConfig node)
        {
            if (!_core.CoreExists)
                throw new FileNotFoundException("未找到 core\\xray.exe，请先运行 scripts\\Integrate-Core.ps1");

            if (Settings.AppFilterMode != "off")
                AppLogger.Warn("应用分流仅 TUN 模式生效，当前分流设置将被忽略");

            // 随机端口（避开常用低端口）与随机认证
            _socksPort = RandomPort();
            _httpPort = _socksPort + 1;
            _authUser = "u" + Guid.NewGuid().ToString("N").Substring(0, 12);
            _authPass = Guid.NewGuid().ToString("N");

            string configJson = XrayConfigBuilder.Build(node, _socksPort, _httpPort, _authUser, _authPass);

            // 先设置系统代理指向 HTTP 入站，再启动核心；任一失败都要回滚
            try
            {
                _core.Start(configJson);
                AppLogger.Info("Xray 核心已启动，节点: " + node.Name + "，本地 SOCKS 端口: " + _socksPort);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Xray 启动失败: " + ex.Message);
                throw;
            }

            try
            {
                _proxy.Enable(_httpPort);
                _proxyActive = true;
                AppLogger.Info("系统代理已设置为 127.0.0.1:" + _httpPort);
            }
            catch (Exception ex)
            {
                AppLogger.Error("设置系统代理失败，正在停止核心: " + ex.Message);
                _core.Stop();
                throw;
            }
            return _socksPort;
        }

        /// <summary>TUN 模式：sing-box 虚拟网卡接管流量 + 按应用分流。返回 0（无本地 SOCKS 端口）。</summary>
        private int StartTun(NodeConfig node)
        {
            if (!IsAdministrator())
                throw new InvalidOperationException("TUN 模式需要管理员权限，请右键以管理员身份运行");
            if (!_tun.CoreExists)
                throw new FileNotFoundException("未找到 core\\sing-box\\sing-box.exe，请先运行 scripts\\Integrate-Tun.ps1");

            var filterPaths = GetFilterPaths();
            string mode = (Settings.AppFilterMode ?? "off").ToLowerInvariant();
            if (SingBoxConfigBuilder.IsDegenerateWhitelist(mode, filterPaths))
                AppLogger.Warn("白名单为空：TUN 已退化为全局直连（所有流量不经过代理）");

            // 防回环：自身 + sing-box 始终直连
            var alwaysDirect = new List<string>();
            try { alwaysDirect.Add(Process.GetCurrentProcess().MainModule.FileName); } catch { }
            alwaysDirect.Add(_tun.SingBoxExePath);

            string configJson = SingBoxConfigBuilder.Build(node, mode, filterPaths, alwaysDirect, ProbeExePath);

            _tun.Start(configJson);
            _proxyActive = false;
            AppLogger.Info("sing-box TUN 已启动，节点: " + node.Name +
                "，分流模式: " + mode + "，名单: " + filterPaths.Count + " 项");
            return 0;
        }

        /// <summary>分流名单（settings 中分号分隔 → 列表）</summary>
        public List<string> GetFilterPaths()
        {
            var list = new List<string>();
            string raw = Settings.AppFilterList ?? "";
            foreach (var p in raw.Split(';'))
            {
                string t = p.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        /// <summary>隧道探测子进程路径（独立路径以便单独路由到 proxy，保证验证真实经过隧道）</summary>
        public string ProbeExePath
        {
            get { return Path.Combine(_dataDir, "probe", "YunTiProProbe.exe"); }
        }

        /// <summary>当前进程是否拥有管理员权限</summary>
        public static bool IsAdministrator()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(id);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>停止代理：恢复原系统代理设置并停止核心</summary>
        public void StopProxy()
        {
            if (_proxyActive)
            {
                try { _proxy.Restore(); AppLogger.Info("系统代理已恢复"); }
                catch (Exception ex) { AppLogger.Error("恢复系统代理失败: " + ex.Message); }
                _proxyActive = false;
            }
            _core.Stop();
            if (_tun.IsRunning)
            {
                _tun.Stop();
                AppLogger.Info("sing-box TUN 已停止");
            }
        }

        /// <summary>远端验证：系统代理模式走本地 SOCKS5；TUN 模式经探测子进程（其流量被路由进隧道）</summary>
        public Task<int> VerifyRemoteAsync()
        {
            if (_tun.IsRunning)
                return VerifyTunnelAsync();
            return NetworkTools.VerifyRemoteViaSocksAsync(_socksPort, _authUser, _authPass, 8000);
        }

        /// <summary>
        /// TUN 远端验证：把自身复制为独立路径的探测子进程运行 --tunnel-probe。
        /// 该进程路径不在防回环直连名单内，且被配置显式路由到 proxy，探测流量真实经过隧道。
        /// </summary>
        private Task<int> VerifyTunnelAsync()
        {
            return Task.Run<int>(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    string self = Process.GetCurrentProcess().MainModule.FileName;
                    string probe = ProbeExePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(probe));
                    // 内容变化才复制，避免每次连接都写盘
                    bool needCopy = !File.Exists(probe) ||
                        new FileInfo(probe).Length != new FileInfo(self).Length;
                    if (needCopy) File.Copy(self, probe, true);

                    var psi = new ProcessStartInfo
                    {
                        FileName = probe,
                        Arguments = "--tunnel-probe",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        if (!p.WaitForExit(10000))
                        {
                            try { p.Kill(); } catch { }
                            return -1;
                        }
                        sw.Stop();
                        return p.ExitCode == 0 ? (int)sw.ElapsedMilliseconds : -1;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("隧道探测子进程不可用: " + ex.Message);
                    return -1;
                }
            });
        }

        /// <summary>应用退出兜底</summary>
        public void Shutdown()
        {
            try { StopProxy(); } catch { }
        }

        // ---------- 设置 ----------

        private string SettingsFile { get { return Path.Combine(_dataDir, "settings.txt"); } }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                ParseSettingsInto(File.ReadAllText(SettingsFile), Settings);
            }
            catch { }
        }

        public void SaveSettings()
        {
            try
            {
                File.WriteAllText(SettingsFile, FormatSettings(Settings), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>解析 settings.txt 内容到既有对象；缺失字段保留默认值（向后兼容旧格式）</summary>
        public static void ParseSettingsInto(string text, AppSettings s)
        {
            if (text == null || s == null) return;
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.StartsWith("AutoStart=")) s.AutoStart = line.Substring(10) == "1";
                else if (line.StartsWith("DisclaimerAccepted=")) s.DisclaimerAccepted = line.Substring(19) == "1";
                else if (line.StartsWith("ProxyMode=")) s.ProxyMode = NormalizeProxyMode(line.Substring(10));
                else if (line.StartsWith("AppFilterMode=")) s.AppFilterMode = NormalizeFilterMode(line.Substring(14));
                else if (line.StartsWith("AppFilterList=")) s.AppFilterList = line.Substring(14).Trim();
            }
        }

        /// <summary>序列化设置为 settings.txt 内容</summary>
        public static string FormatSettings(AppSettings s)
        {
            return "AutoStart=" + (s.AutoStart ? "1" : "0") + "\n" +
                "DisclaimerAccepted=" + (s.DisclaimerAccepted ? "1" : "0") + "\n" +
                "ProxyMode=" + NormalizeProxyMode(s.ProxyMode) + "\n" +
                "AppFilterMode=" + NormalizeFilterMode(s.AppFilterMode) + "\n" +
                "AppFilterList=" + (s.AppFilterList ?? "");
        }

        private static string NormalizeProxyMode(string v)
        {
            return string.Equals((v ?? "").Trim(), "tun", StringComparison.OrdinalIgnoreCase) ? "tun" : "system";
        }

        private static string NormalizeFilterMode(string v)
        {
            string t = (v ?? "").Trim().ToLowerInvariant();
            return (t == "whitelist" || t == "blacklist") ? t : "off";
        }

        /// <summary>通过注册表 Run 键实现开机自启</summary>
        public void ApplyAutoStart()
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (key == null) return;
                    if (Settings.AutoStart)
                    {
                        string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue("YunTiPro", "\"" + exe + "\"");
                    }
                    else
                    {
                        key.DeleteValue("YunTiPro", false);
                    }
                }
                AppLogger.Info("开机自启: " + (Settings.AutoStart ? "开" : "关"));
            }
            catch (Exception ex)
            {
                AppLogger.Error("设置开机自启失败: " + ex.Message);
            }
        }

        // ---------- 工具 ----------

        private static readonly Random Rng = new Random();

        /// <summary>生成 20000-60000 区间随机端口</summary>
        public static int RandomPort()
        {
            return Rng.Next(20000, 60000);
        }

        /// <summary>生成随机认证串</summary>
        public static string RandomToken(int len)
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                var buf = new byte[len];
                rng.GetBytes(buf);
                var sb = new StringBuilder(len);
                foreach (var b in buf) sb.Append("0123456789abcdef"[b & 0xF]);
                return sb.ToString();
            }
        }
    }
}
