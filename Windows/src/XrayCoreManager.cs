using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace YunTiPro
{
    /// <summary>
    /// Xray 核心子进程管理：写入配置 → 启动 → 捕获输出 → 停止。
    /// 日志输出只做状态提示，不记录 UUID/密码/完整配置。
    /// </summary>
    public class XrayCoreManager
    {
        private readonly string _coreExePath;
        private readonly string _coreDir;
        private Process _process;
        private readonly StringBuilder _recentOutput = new StringBuilder();

        public event Action<string> OnOutput;

        public bool IsRunning
        {
            get { return _process != null && !_process.HasExited; }
        }

        public XrayCoreManager(string coreDir)
        {
            _coreDir = coreDir;
            _coreExePath = Path.Combine(coreDir, "xray.exe");
        }

        public bool CoreExists
        {
            get { return File.Exists(_coreExePath); }
        }

        /// <summary>
        /// 启动核心。configJson 为完整 Xray 配置文本。
        /// </summary>
        public void Start(string configJson)
        {
            if (!CoreExists)
                throw new FileNotFoundException("未找到 Xray 核心，请先运行 scripts\\Integrate-Core.ps1", _coreExePath);

            Stop();

            string configPath = Path.Combine(_coreDir, "runtime-config.json");
            File.WriteAllText(configPath, configJson, new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = _coreExePath,
                Arguments = "-config \"" + configPath + "\"",
                WorkingDirectory = _coreDir,
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
        }

        /// <summary>停止核心进程</summary>
        public void Stop()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(5000);
                }
            }
            catch { }
            finally
            {
                if (_process != null) { _process.Dispose(); _process = null; }
            }
        }

        private void Emit(string line)
        {
            // 核心日志中可能含敏感信息，仅透传 warning/error 级别的概要
            lock (_recentOutput)
            {
                if (_recentOutput.Length > 8192) _recentOutput.Remove(0, 4096);
                _recentOutput.AppendLine(line);
            }
            var handler = OnOutput;
            if (handler != null) handler(line);
        }

        public string RecentOutput
        {
            get { lock (_recentOutput) { return _recentOutput.ToString(); } }
        }
    }
}
