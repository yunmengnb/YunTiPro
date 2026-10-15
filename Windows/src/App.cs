using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace YunTiPro
{
    public class App : Application
    {
        private AppServices _svc;
        private FloatingProxyButtonWindow _floatingButton;

        [STAThread]
        public static void Main(string[] args)
        {
            // TUN 隧道探测子进程：无窗口直连探测，退出码 0=隧道通。该进程路径被路由进代理。
            if (args != null && args.Length > 0 && args[0] == "--tunnel-probe")
            {
                int code = 1;
                try
                {
                    int ms = NetworkTools.VerifyRemoteDirectAsync(8000).GetAwaiter().GetResult();
                    code = ms >= 0 ? 0 : 1;
                }
                catch { }
                Environment.Exit(code);
                return;
            }

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        private void InitializeComponent()
        {
            // 纯代码应用：不设置 StartupUri（赋 null 会抛 ArgumentNullException），在 OnStartup 手动建窗
            Startup += OnStartup;
            Exit += OnExit;
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { _svc?.Shutdown(); } catch { } };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                AppLogger.Error("未处理异常: " + (ex?.Message ?? "未知"));
                try { _svc?.Shutdown(); } catch { }
            };
            // 会话结束时也尽量恢复
            SystemEvents.SessionEnding += (s, e) => { try { _svc?.Shutdown(); } catch { } };
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            string appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _svc = new AppServices(appDir);

            var win = new MainWindow(_svc);
            MainWindow = win;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            win.Show();

            if (win.IsVisible)
            {
                _floatingButton = new FloatingProxyButtonWindow(win);
                _floatingButton.Show();
            }
        }

        private void OnExit(object sender, ExitEventArgs e)
        {
            try { _floatingButton?.Close(); } catch { }
            try { _svc?.Shutdown(); } catch { }
        }
    }
}
