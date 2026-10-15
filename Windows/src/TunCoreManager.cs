using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace YunTiPro
{
    /// <summary>
    /// sing-box 核心子进程管理（TUN 模式）：
    /// 复制 wintun.dll 到 sing-box 旁 → 写配置 → sing-box check 校验 → run 启动 →
    /// 捕获输出 → 就绪判定（进程存活 + 无 fatal）→ 停止时杀进程树。
    /// 需要管理员权限（TUN 网卡创建），权限检查由调用方负责。
    /// </summary>
    public class TunCoreManager
    {
        private readonly string _coreDir;
        private readonly string _singBoxDir;
        private readonly string _singBoxExe;
        private readonly string _wintunSrc;
        private Process _process;
        private readonly StringBuilder _recentOutput = new StringBuilder();

        public event Action<string> OnOutput;

        public TunCoreManager(string coreDir)
        {
            _coreDir = coreDir;
            _singBoxDir = Path.Combine(coreDir, "sing-box");
            _singBoxExe = Path.Combine(_singBoxDir, "sing-box.exe");
            _wintunSrc = Path.Combine(coreDir, "wintun", "wintun.dll");
        }

        public string SingBoxExePath { get { return _singBoxExe; } }

        public bool CoreExists
        {
            get { return File.Exists(_singBoxExe); }
        }

        public bool IsRunning
        {
            get { return _process != null && !_process.HasExited; }
        }

        public string RecentOutput
        {
            get { lock (_recentOutput) { return _recentOutput.ToString(); } }
        }

        /// <summary>确保 wintun.dll 位于 sing-box.exe 旁（DLL 搜索路径要求），内容一致则跳过</summary>
        public void EnsureWintun()
        {
            if (!File.Exists(_wintunSrc)) return; // 缺 dll 时让 sing-box 自身报错，信息更直观
            string dest = Path.Combine(_singBoxDir, "wintun.dll");
            try
            {
                if (File.Exists(dest))
                {
                    var s = new FileInfo(_wintunSrc);
                    var d = new FileInfo(dest);
                    if (s.Length == d.Length) return;
                }
                File.Copy(_wintunSrc, dest, true);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("复制 wintun.dll 失败: " + ex.Message);
            }
        }

        /// <summary>用 sing-box check 校验配置文件，返回 (是否通过, 输出摘要)</summary>
        public bool Check(string configPath, out string output)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _singBoxExe,
                Arguments = "check -c \"" + configPath + "\"",
                WorkingDirectory = _singBoxDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                string merged = (stderr + stdout).Trim();
                output = merged.Length > 500 ? merged.Substring(0, 500) : merged;
                return p.ExitCode == 0;
            }
        }

        /// <summary>校验并启动核心。configJson 为完整 sing-box 配置文本。</summary>
        public void Start(string configJson)
        {
            if (!CoreExists)
                throw new FileNotFoundException("未找到 core\\sing-box\\sing-box.exe，请先运行 scripts\\Integrate-Tun.ps1", _singBoxExe);

            Stop();
            EnsureWintun();

            string configPath = Path.Combine(_singBoxDir, "runtime-tun.json");
            File.WriteAllText(configPath, configJson, new UTF8Encoding(false));

            string checkOut;
            if (!Check(configPath, out checkOut))
                throw new InvalidOperationException("sing-box 配置校验未通过: " + checkOut);

            var psi = new ProcessStartInfo
            {
                FileName = _singBoxExe,
                Arguments = "run -c \"" + configPath + "\"",
                WorkingDirectory = _singBoxDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (s, e) => { if (e.Data != null) Emit(e.Data); };
            _process.ErrorDataReceived += (s, e) => { if (e.Data != null) Emit(e.Data); };
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // 就绪判定：等待期间进程退出或输出 fatal 即失败
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2000)
            {
                if (_process.HasExited)
                    throw new InvalidOperationException("sing-box 启动后立即退出: " + TailOutput());
                string recent = RecentOutput;
                if (recent.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Stop();
                    throw new InvalidOperationException("sing-box 报告致命错误: " + TailOutput());
                }
                Thread.Sleep(100);
            }
            if (_process.HasExited)
                throw new InvalidOperationException("sing-box 启动失败: " + TailOutput());
        }

        /// <summary>停止核心进程（含子进程树）</summary>
        public void Stop()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    KillTree(_process.Id);
                    _process.WaitForExit(5000);
                }
            }
            catch { }
            finally
            {
                if (_process != null) { _process.Dispose(); _process = null; }
            }
        }

        /// <summary>taskkill /T /F 杀整棵进程树（sing-box 可能拉起子进程）</summary>
        private static void KillTree(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/PID " + pid + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi)) { p.WaitForExit(3000); }
            }
            catch { }
        }

        private string TailOutput()
        {
            string s = RecentOutput;
            return s.Length > 300 ? s.Substring(s.Length - 300) : s;
        }

        private void Emit(string line)
        {
            lock (_recentOutput)
            {
                if (_recentOutput.Length > 8192) _recentOutput.Remove(0, 4096);
                _recentOutput.AppendLine(line);
            }
            var handler = OnOutput;
            if (handler != null) handler(line);
        }
    }
}
