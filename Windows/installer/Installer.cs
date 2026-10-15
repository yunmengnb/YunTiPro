using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("云梯pro 安装程序")]
[assembly: AssemblyCompany("云梯pro")]
[assembly: AssemblyProduct("云梯pro")]
[assembly: AssemblyVersion("1.1.3.0")]

namespace YunTiProInstaller
{
    internal static class Program
    {
        private const string AppName = "云梯pro";
        private const string Version = "1.1.3";
        private static readonly string DefaultInstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "YunTiPro");

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = args.Length > 0 && string.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase);
            Application.Run(uninstall ? (Form)new UninstallForm() : new InstallForm());
        }

        private sealed class InstallForm : Form
        {
            private readonly Button _install;
            private readonly CheckBox _desktop;
            private readonly TextBox _installPath;
            private readonly Button _browse;
            private readonly Label _status;
            private readonly ProgressBar _progress;

            public InstallForm()
            {
                Text = AppName + " 安装程序";
                ClientSize = new Size(520, 390);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                BackColor = Color.FromArgb(6, 15, 30);
                ForeColor = Color.White;
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

                Controls.Add(new Label { Text = "云梯pro", Font = new Font("Microsoft YaHei UI", 25, FontStyle.Bold), ForeColor = Color.FromArgb(34, 211, 238), AutoSize = true, Location = new Point(38, 32) });
                Controls.Add(new Label { Text = "安全、轻量的代理连接工具", Font = new Font("Microsoft YaHei UI", 10), ForeColor = Color.FromArgb(148, 163, 184), AutoSize = true, Location = new Point(42, 82) });
                Controls.Add(new Label { Text = "版本 " + Version + "  ·  支持 Windows 7–11", Font = new Font("Microsoft YaHei UI", 9), ForeColor = Color.FromArgb(103, 232, 249), AutoSize = true, Location = new Point(42, 116) });

                Controls.Add(new Label { Text = "安装位置", AutoSize = true, Location = new Point(42, 157), ForeColor = Color.FromArgb(226, 232, 240) });
                _installPath = new TextBox { Text = DefaultInstallDir, Location = new Point(42, 180), Size = new Size(350, 28), BackColor = Color.FromArgb(15, 35, 61), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
                Controls.Add(_installPath);
                _browse = new Button { Text = "浏览…", Location = new Point(401, 178), Size = new Size(77, 29), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(15, 35, 61), ForeColor = Color.FromArgb(103, 232, 249) };
                _browse.FlatAppearance.BorderColor = Color.FromArgb(34, 211, 238);
                _browse.Click += BrowseClick;
                Controls.Add(_browse);
                _desktop = new CheckBox { Text = "创建桌面快捷方式", Checked = true, AutoSize = true, Location = new Point(42, 229), ForeColor = Color.FromArgb(226, 232, 240), BackColor = Color.Transparent };
                Controls.Add(_desktop);
                _status = new Label { Text = "可自定义安装目录；受保护目录可能需要管理员权限", AutoSize = true, Location = new Point(42, 267), ForeColor = Color.FromArgb(148, 163, 184) };
                Controls.Add(_status);
                _progress = new ProgressBar { Location = new Point(42, 297), Size = new Size(436, 5), Style = ProgressBarStyle.Marquee, Visible = false };
                Controls.Add(_progress);
                _install = new Button { Text = "立即安装", Location = new Point(338, 328), Size = new Size(140, 38), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(8, 145, 178), ForeColor = Color.White };
                _install.FlatAppearance.BorderColor = Color.FromArgb(34, 211, 238);
                _install.Click += InstallClick;
                Controls.Add(_install);
            }

            private void BrowseClick(object sender, EventArgs e)
            {
                using (var dialog = new FolderBrowserDialog
                {
                    Description = "选择云梯pro安装目录",
                    SelectedPath = Directory.Exists(_installPath.Text) ? _installPath.Text : DefaultInstallDir,
                    ShowNewFolderButton = true
                })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        _installPath.Text = Path.Combine(dialog.SelectedPath, "YunTiPro");
                }
            }

            private async void InstallClick(object sender, EventArgs e)
            {
                string installDir;
                try
                {
                    installDir = NormalizeInstallPath(_installPath.Text);
                }
                catch (Exception ex)
                {
                    _status.Text = "安装目录无效：" + ex.Message;
                    return;
                }
                _install.Enabled = false;
                _browse.Enabled = false;
                _installPath.Enabled = false;
                _progress.Visible = true;
                _status.Text = "正在安装组件…";
                try
                {
                    await System.Threading.Tasks.Task.Run(() => Install(installDir, _desktop.Checked));
                    _progress.Visible = false;
                    _status.Text = "安装完成";
                    _install.Text = "启动云梯pro";
                    _install.Enabled = true;
                    _install.Click -= InstallClick;
                    _install.Click += delegate { Process.Start(Path.Combine(installDir, "YunTiPro.exe")); Close(); };
                }
                catch (Exception ex)
                {
                    _progress.Visible = false;
                    _install.Enabled = true;
                    _browse.Enabled = true;
                    _installPath.Enabled = true;
                    _status.Text = "安装失败：" + ex.Message;
                }
            }
        }

        private sealed class UninstallForm : Form
        {
            public UninstallForm()
            {
                Text = "卸载" + AppName;
                ClientSize = new Size(420, 190);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                BackColor = Color.FromArgb(6, 15, 30);
                ForeColor = Color.White;
                Controls.Add(new Label { Text = "确定卸载云梯pro？", Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold), AutoSize = true, Location = new Point(32, 30) });
                Controls.Add(new Label { Text = "节点和设置数据默认保留。", AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184), Location = new Point(35, 76) });
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(200, 125), Size = new Size(85, 32) };
                var remove = new Button { Text = "卸载", Location = new Point(299, 125), Size = new Size(85, 32), BackColor = Color.FromArgb(190, 24, 93), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                remove.Click += delegate { Uninstall(); Close(); };
                Controls.Add(cancel);
                Controls.Add(remove);
                CancelButton = cancel;
            }
        }

        private static string NormalizeInstallPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("请选择安装目录");
            string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));
            string root = Path.GetPathRoot(path);
            if (string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root == null ? "" : root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("不能直接安装到磁盘根目录");
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void Install(string installDir, bool desktopShortcut)
        {
            foreach (var process in Process.GetProcessesByName("YunTiPro"))
                try { process.Kill(); process.WaitForExit(3000); } catch { }
            Directory.CreateDirectory(installDir);
            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(installDir, entry.FullName));
                    string root = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装包路径无效");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
            File.Copy(Application.ExecutablePath, Path.Combine(installDir, "Uninstall.exe"), true);
            string exe = Path.Combine(installDir, "YunTiPro.exe");
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
            CreateShortcut(startMenu, exe, installDir, AppName);
            if (desktopShortcut)
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"), exe, installDir, AppName);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\YunTiPro"))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", Version);
                key.SetValue("DisplayIcon", exe);
                key.SetValue("Publisher", AppName);
                key.SetValue("InstallLocation", installDir);
                key.SetValue("UninstallString", "\"" + Path.Combine(installDir, "Uninstall.exe") + "\" /uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        private static void Uninstall()
        {
            string installDir = Path.GetDirectoryName(Application.ExecutablePath);
            foreach (var name in new[] { "YunTiPro", "xray", "sing-box" })
                foreach (var process in Process.GetProcessesByName(name)) try { process.Kill(); } catch { }
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"));
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\YunTiPro", false);
            string command = "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + installDir + "\"";
            Process.Start(new ProcessStartInfo("cmd.exe", command) { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

        private static void CreateShortcut(string shortcutPath, string target, string workingDirectory, string description)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(type);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.Description = description;
            shortcut.IconLocation = target + ",0";
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
