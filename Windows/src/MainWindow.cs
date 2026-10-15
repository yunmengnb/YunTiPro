using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using DrawingBitmap = System.Drawing.Bitmap;

namespace YunTiPro
{
    /// <summary>
    /// 主窗口：左侧导航（节点/日志/设置/关于）+ 顶部栏 + 首页节点卡片。
    /// 全部使用 C# 代码构建，无 XAML。
    /// </summary>
    public class MainWindow : Window
    {
        public event Action<string> ConnectionStateChanged;

        // ---------- 服务 ----------
        private readonly AppServices _svc;

        // ---------- 状态 ----------
        private bool _connected;
        private NodeConfig _activeNode;
        private NodeConfig _selectedNode;
        private readonly Dictionary<string, FrameworkElement> _cardMap = new Dictionary<string, FrameworkElement>();

        // ---------- UI 元素 ----------
        private TextBlock _statusText;
        private TextBlock _speedHintText;
        private Button _powerButton;
        private TextBlock _powerLabel;
        private StackPanel _nodeListPanel;
        private TextBlock _logBox;
        private Grid _contentHost;
        private readonly Dictionary<string, UIElement> _pages = new Dictionary<string, UIElement>();
        private readonly List<Button> _navButtons = new List<Button>();

        // ---------- 分流页状态 ----------
        private List<InstalledAppInfo> _allApps = new List<InstalledAppInfo>();
        private readonly HashSet<string> _checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private StackPanel _appListPanel;
        private TextBox _appSearchBox;
        private TextBlock _appListStatus;
        private TextBlock _filterModeDesc;
        private TextBlock _filterDirtyHint;
        private Border _filterTunHint;
        private RadioButton _modeOff, _modeWhite, _modeBlack;
        private bool _appsLoading;

        // ---------- 设置页代理模式 ----------
        private RadioButton _proxyModeSystem, _proxyModeTun;
        private TextBlock _tunAdminHint;
        private Button _restartAdminBtn;

        // ---------- 主题色 ----------
        private static readonly Brush CPrimary = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        private static readonly Brush CPrimarySoft = new SolidColorBrush(Color.FromRgb(0xE8, 0xEF, 0xFD));
        private static readonly Brush CBg = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
        private static readonly Brush CCard = Brushes.White;
        private static readonly Brush CText = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
        private static readonly Brush CTextSec = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
        private static readonly Brush CBorder = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
        private static readonly Brush CGreen = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
        private static readonly Brush CRed = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));

        public MainWindow(AppServices svc)
        {
            _svc = svc;
            Title = "云梯pro 电脑版";
            Width = 980;
            Height = 640;
            MinWidth = 760;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = CBg;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            LoadWindowIcon();

            BuildLayout();
            SwitchPage("节点");
            RefreshNodeList();
            UpdateStatusUi();

            // 应用退出时兜底恢复系统代理
            Closing += (s, e) => { _svc.Shutdown(); };

            // 首次启动须同意免责协议，不同意则退出
            Loaded += (s, e) =>
            {
                if (!_svc.Settings.DisclaimerAccepted && !ShowDisclaimerConsent()) Close();
            };
        }

        // ================= 布局 =================

        private void BuildLayout()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---- 左侧导航 ----
            var nav = new Border { Background = CCard, BorderBrush = CBorder, BorderThickness = new Thickness(0, 0, 1, 0) };
            var navLayout = new Grid { Margin = new Thickness(12, 20, 12, 14) };
            navLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            navLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            navLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var navTitle = new TextBlock
            {
                Text = "云梯pro",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = CPrimary,
                Margin = new Thickness(8, 0, 0, 18)
            };
            Grid.SetRow(navTitle, 0);
            navLayout.Children.Add(navTitle);

            var navButtons = new StackPanel();
            foreach (var name in new[] { "节点", "分流", "日志", "设置", "关于" })
            {
                var btn = MakeNavButton(name);
                _navButtons.Add(btn);
                navButtons.Children.Add(btn);
            }
            Grid.SetRow(navButtons, 1);
            navLayout.Children.Add(navButtons);

            var powerPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 12, 0, 0) };
            _powerButton = new Button { Width = 64, Height = 64, Cursor = Cursors.Hand };
            _powerButton.Template = MakeCircleButtonTemplate();
            _powerButton.Click += async (s, e) => await ToggleConnectionAsync();
            _powerLabel = new TextBlock
            {
                Text = "点击启动",
                FontSize = 12,
                Foreground = CTextSec,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            powerPanel.Children.Add(_powerButton);
            powerPanel.Children.Add(_powerLabel);
            Grid.SetRow(powerPanel, 2);
            navLayout.Children.Add(powerPanel);
            nav.Child = navLayout;
            Grid.SetColumn(nav, 0);
            root.Children.Add(nav);

            // ---- 右侧：顶部栏 + 内容 ----
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 顶部栏
            var topBar = new Border
            {
                Background = CCard,
                BorderBrush = CBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 0, 20, 0)
            };
            var topPanel = new Grid();
            var titleBlock = new TextBlock
            {
                Text = "云梯pro 电脑版",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = CText,
                VerticalAlignment = VerticalAlignment.Center
            };
            topPanel.Children.Add(titleBlock);

            var topRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            // 刷新按钮（版本号前）
            var refreshBtn = MakeSmallButton("⟳ 测速", CPrimary, Brushes.White);
            refreshBtn.Click += async (s, e) => await RunSpeedTestAsync();
            topRight.Children.Add(refreshBtn);
            var verBlock = new TextBlock
            {
                Text = "v" + AppServices.Version,
                Foreground = CTextSec,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            topRight.Children.Add(verBlock);
            topPanel.Children.Add(topRight);
            topBar.Child = topPanel;
            Grid.SetRow(topBar, 0);
            right.Children.Add(topBar);

            // 内容区
            _contentHost = new Grid { Margin = new Thickness(20) };
            _pages["节点"] = BuildNodesPage();
            _pages["分流"] = BuildFilterPage();
            _pages["日志"] = BuildLogPage();
            _pages["设置"] = BuildSettingsPage();
            _pages["关于"] = BuildAboutPage();
            foreach (var kv in _pages)
            {
                kv.Value.Visibility = Visibility.Collapsed;
                _contentHost.Children.Add(kv.Value);
            }
            Grid.SetRow(_contentHost, 1);
            right.Children.Add(_contentHost);

            Grid.SetColumn(right, 1);
            root.Children.Add(right);

            Content = root;
        }

        private Button MakeNavButton(string name)
        {
            var btn = new Button
            {
                Content = name,
                Height = 40,
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 0, 0, 0),
                FontSize = 14,
                Foreground = CText,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Tag = name
            };
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(14, 0, 0, 0));
            border.AppendChild(cp);
            template.VisualTree = border;
            btn.Template = template;

            btn.Click += (s, e) => SwitchPage(name);
            return btn;
        }

        private void SwitchPage(string name)
        {
            foreach (var kv in _pages)
                kv.Value.Visibility = kv.Key == name ? Visibility.Visible : Visibility.Collapsed;
            foreach (var btn in _navButtons)
            {
                bool active = (string)btn.Tag == name;
                btn.Background = active ? CPrimarySoft : Brushes.Transparent;
                btn.Foreground = active ? CPrimary : CText;
                btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        // ================= 节点页 =================

        private UIElement BuildNodesPage()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 状态文字
            _statusText = new TextBlock
            {
                Text = "未连接",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = CTextSec,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 8)
            };
            Grid.SetRow(_statusText, 0);
            grid.Children.Add(_statusText);

            // 节点列表（卡片流式布局 + 工具按钮行）
            var listArea = new Grid();
            listArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var toolRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var addBtn = MakeSmallButton("+ 新增节点", CPrimary, Brushes.White);
            addBtn.Click += (s, e) => ShowEditDialog(null);
            var importBtn = MakeSmallButton("导入链接", Brushes.White, CPrimary);
            importBtn.BorderBrush = CPrimary;
            importBtn.BorderThickness = new Thickness(1);
            importBtn.Margin = new Thickness(8, 0, 0, 0);
            importBtn.Click += (s, e) => ShowImportEntry();
            toolRow.Children.Add(addBtn);
            toolRow.Children.Add(importBtn);
            // 测速提示文字（默认隐藏，仅点测速按钮时显示）
            _speedHintText = new TextBlock
            {
                Text = "",
                Foreground = CTextSec,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            toolRow.Children.Add(_speedHintText);
            Grid.SetRow(toolRow, 0);
            listArea.Children.Add(toolRow);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _nodeListPanel = new StackPanel();
            scroll.Content = _nodeListPanel;
            Grid.SetRow(scroll, 1);
            listArea.Children.Add(scroll);

            Grid.SetRow(listArea, 1);
            grid.Children.Add(listArea);
            return grid;
        }

        private ControlTemplate MakeCircleButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var ellipse = new FrameworkElementFactory(typeof(Ellipse));
            ellipse.SetValue(Ellipse.FillProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            ellipse.SetValue(Ellipse.StrokeProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            ellipse.SetValue(Ellipse.StrokeThicknessProperty, 2.0);
            var grid = new FrameworkElementFactory(typeof(Grid));
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetValue(TextBlock.TextProperty, new TemplateBindingExtension(Button.ContentProperty));
            tb.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            tb.SetValue(TextBlock.FontSizeProperty, 15.0);
            tb.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            tb.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(tb);
            // 用 Grid 叠放 ellipse 和文字
            var outer = new FrameworkElementFactory(typeof(Grid));
            outer.AppendChild(ellipse);
            outer.AppendChild(grid);
            template.VisualTree = outer;
            return template;
        }

        /// <summary>重建节点卡片列表</summary>
        public void RefreshNodeList()
        {
            _nodeListPanel.Children.Clear();
            _cardMap.Clear();

            if (_svc.Nodes.Count == 0)
            {
                _nodeListPanel.Children.Add(new TextBlock
                {
                    Text = "暂无节点，请点击「新增节点」或「导入链接」",
                    Foreground = CTextSec,
                    Margin = new Thickness(0, 20, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            foreach (var node in _svc.Nodes)
            {
                var card = BuildNodeCard(node);
                _cardMap[node.Id] = card;
                _nodeListPanel.Children.Add(card);
            }
        }

        private FrameworkElement BuildNodeCard(NodeConfig node)
        {
            // 小尺寸卡片：一行一个，左侧选中圆点 + 名称/副标题 + 协议徽标 + 延迟 + 编辑/删除
            var border = new Border
            {
                Background = CCard,
                BorderBrush = _selectedNode == node ? CPrimary : CBorder,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Tag = node
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 选中圆点
            var dot = new Ellipse
            {
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = _selectedNode == node ? CPrimary : Brushes.Transparent,
                Stroke = _selectedNode == node ? CPrimary : CBorder,
                StrokeThickness = 2,
                Name = "dot"
            };
            Grid.SetColumn(dot, 0);
            grid.Children.Add(dot);

            // 中部信息
            var mid = new StackPanel { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            var nameBlock = new TextBlock
            {
                Text = node.Name,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = CText,
                VerticalAlignment = VerticalAlignment.Center
            };
            nameRow.Children.Add(nameBlock);
            // 协议徽标
            var badge = new Border
            {
                Background = CPrimarySoft,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = node.Protocol.ToUpperInvariant(), FontSize = 10, Foreground = CPrimary, FontWeight = FontWeights.SemiBold }
            };
            nameRow.Children.Add(badge);
            mid.Children.Add(nameRow);

            var subRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            // 副标题：网络·TLS（不显示 IP/端口）
            subRow.Children.Add(new TextBlock { Text = node.Subtitle, FontSize = 11, Foreground = CTextSec });
            // 左下角延迟
            string latencyText;
            Brush latencyColor;
            if (node.Protocol == "dokodemo-door") { latencyText = "不适用"; latencyColor = CTextSec; }
            else if (node.LatencyTesting) { latencyText = "测速中…"; latencyColor = CTextSec; }
            else if (node.LatencyMs >= 0) { latencyText = "TCP " + node.LatencyMs + " ms"; latencyColor = node.LatencyMs < 500 ? CGreen : new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)); }
            else { latencyText = "未测速"; latencyColor = CTextSec; }
            subRow.Children.Add(new TextBlock { Text = "    " + latencyText, FontSize = 11, Foreground = latencyColor });
            mid.Children.Add(subRow);

            Grid.SetColumn(mid, 1);
            grid.Children.Add(mid);

            // 右侧分享/编辑/删除小按钮
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var shareBtn = MakeIconButton("分享");
            shareBtn.Margin = new Thickness(0, 0, 6, 0);
            shareBtn.Click += (s, e) => { e.Handled = true; ShowShareEntry(node); };
            var editBtn = MakeIconButton("编辑");
            editBtn.Click += (s, e) => { e.Handled = true; ShowEditDialog(node); };
            var delBtn = MakeIconButton("删除");
            delBtn.Margin = new Thickness(6, 0, 0, 0);
            delBtn.Click += (s, e) =>
            {
                e.Handled = true;
                if (MessageBox.Show("确定删除节点「" + node.Name + "」？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _svc.DeleteNode(node);
                    if (_selectedNode == node) _selectedNode = null;
                    RefreshNodeList();
                }
            };
            right.Children.Add(shareBtn);
            right.Children.Add(editBtn);
            right.Children.Add(delBtn);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            border.Child = grid;

            // 点击卡片选中；连接中点击其他卡片自动切换
            border.MouseLeftButtonUp += async (s, e) =>
            {
                if (_selectedNode == node && !_connected) return;
                _selectedNode = node;
                _svc.SelectedNodeId = node.Id;
                _svc.SaveNodes();
                RefreshCardSelection();
                if (_connected && _activeNode != node)
                {
                    await SwitchActiveNodeAsync(node);
                }
            };

            return border;
        }

        private void RefreshCardSelection()
        {
            // 简单起见整体重建
            RefreshNodeList();
        }

        private Button MakeIconButton(string text)
        {
            return new Button
            {
                Content = text,
                FontSize = 11,
                Padding = new Thickness(8, 2, 8, 2),
                Background = Brushes.Transparent,
                Foreground = CTextSec,
                BorderBrush = CBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        // ================= 连接逻辑 =================

        public Task ToggleConnectionFromExternalAsync()
        {
            return ToggleConnectionAsync();
        }

        private async Task ToggleConnectionAsync()
        {
            if (_connected)
            {
                _svc.StopProxy();
                _connected = false;
                _activeNode = null;
                if (_filterDirtyHint != null) _filterDirtyHint.Visibility = Visibility.Collapsed;
                UpdateStatusUi("已停止");
                return;
            }
            if (_selectedNode == null)
            {
                MessageBox.Show("请先选择一个节点", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!_selectedNode.IsOutboundCapable)
            {
                MessageBox.Show("dokodemo-door 仅用于本机入站转发，不能作为代理出口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await StartNodeAsync(_selectedNode);
        }

        private async Task SwitchActiveNodeAsync(NodeConfig node)
        {
            if (!node.IsOutboundCapable)
            {
                UpdateStatusUi("该节点不能作为出口（dokodemo-door）");
                return;
            }
            UpdateStatusUi("切换节点中…");
            await StartNodeAsync(node);
        }

        private async Task StartNodeAsync(NodeConfig node)
        {
            try
            {
                _powerButton.IsEnabled = false;
                UpdateStatusUi("正在连接 " + node.Name + " …");

                // 停止旧核心 → 生成新配置 → 启动新核心 → 设置系统代理
                int socksPort = _svc.StartProxy(node);
                _connected = true;
                _activeNode = node;
                if (_filterDirtyHint != null) _filterDirtyHint.Visibility = Visibility.Collapsed;
                UpdateStatusUi("已连接 · 验证远端…");

                // 远端验证
                int ms = await _svc.VerifyRemoteAsync();
                if (ms >= 0)
                    UpdateStatusUi("已连接 · 远端正常 " + ms + " ms");
                else
                    UpdateStatusUi("已连接 · 远端验证失败");
            }
            catch (Exception ex)
            {
                AppLogger.Error("连接失败: " + ex.Message);
                _svc.StopProxy();
                _connected = false;
                _activeNode = null;
                UpdateStatusUi("连接失败");
                MessageBox.Show("连接失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _powerButton.IsEnabled = true;
            }
        }

        private void UpdateStatusUi(string overrideText = null)
        {
            if (overrideText != null) _statusText.Text = overrideText;
            else _statusText.Text = _connected ? "已连接" : "未连接";

            bool on = _statusText.Text.StartsWith("已连接");
            _statusText.Foreground = on ? CGreen : (_statusText.Text.Contains("失败") ? CRed : CTextSec);
            _powerButton.Background = on ? CRed : CPrimary;
            _powerButton.BorderBrush = on ? CRed : CPrimary;
            _powerButton.Content = on ? "停止" : "启动";
            _powerLabel.Text = on ? "点击停止" : "点击启动";
            ConnectionStateChanged?.Invoke(_statusText.Text);
        }

        // ================= 测速 =================

        private async Task RunSpeedTestAsync()
        {
            if (_svc.Nodes.Count == 0) return;
            _speedHintText.Text = "正在测速（最多4并发，超时3秒）…";
            _speedHintText.Visibility = Visibility.Visible;
            foreach (var n in _svc.Nodes) { n.LatencyTesting = true; }
            RefreshNodeList();

            await NetworkTools.TestAllAsync(_svc.Nodes, (node, ms) =>
            {
                node.LatencyMs = ms;
                node.LatencyTesting = false;
                Dispatcher.Invoke(() => RefreshNodeList());
            }, 4, 3000);

            foreach (var n in _svc.Nodes) n.LatencyTesting = false;
            Dispatcher.Invoke(() => RefreshNodeList());
            _speedHintText.Text = "测速完成";
            await Task.Delay(2000);
            _speedHintText.Visibility = Visibility.Collapsed;
            _speedHintText.Text = "";
        }

        // ================= 分流页 =================

        private UIElement BuildFilterPage()
        {
            foreach (var p in _svc.GetFilterPaths()) _checkedPaths.Add(p);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 提示条
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 模式 + 说明
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 搜索/按钮
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 列表

            // 顶部提示条：非 TUN 模式时分流不生效
            _filterTunHint = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12),
                Child = new TextBlock
                {
                    Text = "当前为系统代理模式 —— 应用分流仅在 TUN 模式下生效（可在「设置」页切换代理模式）。",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            Grid.SetRow(_filterTunHint, 0);
            grid.Children.Add(_filterTunHint);

            // 模式选择
            var modePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            modePanel.Children.Add(MakeSectionTitle("应用分流"));
            string mode = (_svc.Settings.AppFilterMode ?? "off").ToLowerInvariant();
            _modeOff = MakeModeRadio(modePanel, "关闭分流（全部流量走代理）", mode == "off");
            _modeWhite = MakeModeRadio(modePanel, "白名单：仅勾选的应用走代理，其余直连", mode == "whitelist");
            _modeBlack = MakeModeRadio(modePanel, "黑名单：勾选的应用直连，其余走代理", mode == "blacklist");
            _filterModeDesc = new TextBlock
            {
                FontSize = 12,
                Foreground = CTextSec,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            modePanel.Children.Add(_filterModeDesc);
            Grid.SetRow(modePanel, 1);
            grid.Children.Add(modePanel);
            UpdateFilterModeUi();

            // 搜索 + 操作按钮
            var toolRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _appSearchBox = new TextBox
            {
                Width = 220,
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            _appSearchBox.TextChanged += (s, e) => RebuildAppList();
            toolRow.Children.Add(_appSearchBox);
            var addExeBtn = MakeSmallButton("手动添加 exe...", CPrimary, Brushes.White);
            addExeBtn.Margin = new Thickness(10, 0, 0, 0);
            addExeBtn.Click += (s, e) => AddExeManually();
            toolRow.Children.Add(addExeBtn);
            var refreshBtn = MakeSmallButton("刷新列表", Brushes.White, CPrimary);
            refreshBtn.BorderBrush = CPrimary;
            refreshBtn.BorderThickness = new Thickness(1);
            refreshBtn.Margin = new Thickness(8, 0, 0, 0);
            refreshBtn.Click += (s, e) => LoadAppsAsync();
            toolRow.Children.Add(refreshBtn);
            _appListStatus = new TextBlock
            {
                FontSize = 12,
                Foreground = CTextSec,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };
            toolRow.Children.Add(_appListStatus);
            Grid.SetRow(toolRow, 2);
            grid.Children.Add(toolRow);

            // 应用列表 + 修改提示
            var listArea = new Grid();
            listArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _filterDirtyHint = new TextBlock
            {
                Text = "分流设置已修改，将在重新连接后生效。",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)),
                Margin = new Thickness(0, 0, 0, 6),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_filterDirtyHint, 0);
            listArea.Children.Add(_filterDirtyHint);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _appListPanel = new StackPanel();
            scroll.Content = _appListPanel;
            Grid.SetRow(scroll, 1);
            listArea.Children.Add(scroll);
            Grid.SetRow(listArea, 3);
            grid.Children.Add(listArea);

            UpdateFilterTunHint();
            LoadAppsAsync();
            return grid;
        }

        private RadioButton MakeModeRadio(StackPanel parent, string text, bool isChecked)
        {
            var rb = new RadioButton
            {
                Content = text,
                FontSize = 13,
                Foreground = CText,
                Margin = new Thickness(0, 3, 0, 3),
                IsChecked = isChecked,
                GroupName = "AppFilterMode"
            };
            rb.Click += (s, e) => OnFilterModeChanged();
            parent.Children.Add(rb);
            return rb;
        }

        private void OnFilterModeChanged()
        {
            string mode = _modeWhite.IsChecked == true ? "whitelist" : (_modeBlack.IsChecked == true ? "blacklist" : "off");
            _svc.Settings.AppFilterMode = mode;
            _svc.SaveSettings();
            UpdateFilterModeUi();
            NotifyFilterChanged();
        }

        /// <summary>分流设置变化：连接中仅提示需重连生效</summary>
        private void NotifyFilterChanged()
        {
            if (_filterDirtyHint != null)
                _filterDirtyHint.Visibility = _connected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateFilterModeUi()
        {
            string mode = (_svc.Settings.AppFilterMode ?? "off").ToLowerInvariant();
            string desc;
            if (mode == "whitelist")
            {
                desc = _checkedPaths.Count == 0
                    ? "白名单为空：所有应用流量将直连（等同于关闭代理效果）。请先在下方勾选需要走代理的应用。"
                    : "仅下方勾选的应用流量走代理，其余应用直连。";
            }
            else if (mode == "blacklist")
            {
                desc = "下方勾选的应用直连（不经过代理），其余应用走代理。";
            }
            else
            {
                desc = "关闭分流：TUN 模式下全部应用流量都走代理（本程序与核心自身除外，始终直连以防回环）。";
            }
            if (_filterModeDesc != null) _filterModeDesc.Text = desc;
        }

        private void UpdateFilterTunHint()
        {
            if (_filterTunHint != null)
                _filterTunHint.Visibility =
                    string.Equals(_svc.Settings.ProxyMode, "tun", StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void LoadAppsAsync()
        {
            if (_appsLoading) return;
            _appsLoading = true;
            _appListStatus.Text = "正在加载应用列表…";
            try
            {
                var apps = await Task.Run(() => InstalledApps.GetApps());
                // 手动添加过但枚举不到的路径保留显示
                var known = new HashSet<string>(apps.Select(a => a.ExePath), StringComparer.OrdinalIgnoreCase);
                foreach (var p in _checkedPaths)
                {
                    if (!known.Contains(p) && System.IO.File.Exists(p))
                        apps.Add(new InstalledAppInfo { Name = System.IO.Path.GetFileNameWithoutExtension(p) + "（手动添加）", ExePath = p });
                }
                _allApps = apps;
                _appListStatus.Text = "共 " + apps.Count + " 个应用";
                RebuildAppList();
            }
            catch (Exception ex)
            {
                _appListStatus.Text = "加载失败: " + ex.Message;
            }
            finally { _appsLoading = false; }
        }

        private void RebuildAppList()
        {
            if (_appListPanel == null) return;
            _appListPanel.Children.Clear();
            string filter = (_appSearchBox != null ? _appSearchBox.Text : "").Trim();

            foreach (var app in _allApps)
            {
                if (filter.Length > 0 &&
                    app.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    app.ExePath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _appListPanel.Children.Add(BuildAppRow(app));
            }
            if (_appListPanel.Children.Count == 0)
            {
                _appListPanel.Children.Add(new TextBlock
                {
                    Text = filter.Length > 0 ? "没有匹配的应用" : "未发现应用，可点击「手动添加 exe...」",
                    Foreground = CTextSec,
                    FontSize = 12,
                    Margin = new Thickness(0, 12, 0, 0)
                });
            }
        }

        private UIElement BuildAppRow(InstalledAppInfo app)
        {
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = app.Name, FontSize = 13, Foreground = CText });
            info.Children.Add(new TextBlock { Text = app.ExePath, FontSize = 11, Foreground = CTextSec });

            var cb = new CheckBox
            {
                Content = info,
                Margin = new Thickness(0, 4, 0, 4),
                IsChecked = _checkedPaths.Contains(app.ExePath),
                Tag = app.ExePath
            };
            cb.Checked += (s, e) => OnAppToggled(app.ExePath, true);
            cb.Unchecked += (s, e) => OnAppToggled(app.ExePath, false);
            return cb;
        }

        private void OnAppToggled(string path, bool isChecked)
        {
            if (isChecked) _checkedPaths.Add(path); else _checkedPaths.Remove(path);
            _svc.Settings.AppFilterList = string.Join(";", _checkedPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
            _svc.SaveSettings();
            UpdateFilterModeUi();
            NotifyFilterChanged();
        }

        private void AddExeManually()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择应用程序",
                Filter = "应用程序 (*.exe)|*.exe",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                string path = dlg.FileName;
                if (!_allApps.Any(a => string.Equals(a.ExePath, path, StringComparison.OrdinalIgnoreCase)))
                {
                    _allApps.Add(new InstalledAppInfo
                    {
                        Name = System.IO.Path.GetFileNameWithoutExtension(path) + "（手动添加）",
                        ExePath = path
                    });
                }
                _checkedPaths.Add(path);
                _svc.Settings.AppFilterList = string.Join(";", _checkedPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
                _svc.SaveSettings();
                UpdateFilterModeUi();
                NotifyFilterChanged();
                RebuildAppList();
            }
        }

        // ================= 日志页 =================

        private UIElement BuildLogPage()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var clearBtn = MakeSmallButton("清空显示", Brushes.White, CPrimary);
            clearBtn.BorderBrush = CPrimary;
            clearBtn.BorderThickness = new Thickness(1);
            clearBtn.HorizontalAlignment = HorizontalAlignment.Right;
            clearBtn.Margin = new Thickness(0, 0, 0, 8);
            clearBtn.Click += (s, e) => { _logBox.Text = ""; };
            Grid.SetRow(clearBtn, 0);
            grid.Children.Add(clearBtn);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = CCard };
            _logBox = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = CText,
                Padding = new Thickness(10),
                TextWrapping = TextWrapping.Wrap
            };
            scroll.Content = _logBox;
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            AppLogger.OnLog += line =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_logBox.Text.Length > 20000) _logBox.Text = "";
                    _logBox.Text += line + "\n";
                    scroll.ScrollToEnd();
                }));
            };
            return grid;
        }

        // ================= 设置页 / 关于页 =================

        private UIElement BuildSettingsPage()
        {
            var panel = new StackPanel { MaxWidth = 520 };
            panel.Children.Add(MakeSectionTitle("系统设置"));

            var autoStart = new CheckBox { Content = "开机自动启动", Margin = new Thickness(0, 8, 0, 8), IsChecked = _svc.Settings.AutoStart };
            autoStart.Click += (s, e) => { _svc.Settings.AutoStart = autoStart.IsChecked == true; _svc.ApplyAutoStart(); _svc.SaveSettings(); };
            panel.Children.Add(autoStart);

            // ---- 代理模式 ----
            panel.Children.Add(MakeSectionTitle("代理模式"));
            bool isTun = string.Equals(_svc.Settings.ProxyMode, "tun", StringComparison.OrdinalIgnoreCase);
            _proxyModeSystem = new RadioButton
            {
                Content = "系统代理（推荐，兼容性好，仅接管遵循系统代理的应用）",
                GroupName = "ProxyMode",
                IsChecked = !isTun,
                Margin = new Thickness(0, 4, 0, 4),
                FontSize = 13
            };
            _proxyModeTun = new RadioButton
            {
                Content = "TUN 模式（虚拟网卡接管全机流量，支持按应用分流，需管理员权限）",
                GroupName = "ProxyMode",
                IsChecked = isTun,
                Margin = new Thickness(0, 4, 0, 4),
                FontSize = 13
            };
            _proxyModeSystem.Click += (s, e) => OnProxyModeChanged();
            _proxyModeTun.Click += (s, e) => OnProxyModeChanged();
            panel.Children.Add(_proxyModeSystem);
            panel.Children.Add(_proxyModeTun);

            _tunAdminHint = new TextBlock
            {
                Text = "当前未以管理员身份运行，TUN 模式无法启动。",
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 4)
            };
            panel.Children.Add(_tunAdminHint);

            _restartAdminBtn = MakeSmallButton("以管理员身份重启", new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)), Brushes.White);
            _restartAdminBtn.HorizontalAlignment = HorizontalAlignment.Left;
            _restartAdminBtn.Click += (s, e) => RestartAsAdministrator();
            panel.Children.Add(_restartAdminBtn);
            UpdateProxyModeUi();

            var coreInfo = new TextBlock
            {
                Text = "Xray 核心目录: core\\ （更换 core\\xray.exe 可使用新版核心，但 Win7 需 Go≤1.20 编译版本）",
                Foreground = CTextSec,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            panel.Children.Add(coreInfo);
            return panel;
        }

        private void OnProxyModeChanged()
        {
            _svc.Settings.ProxyMode = _proxyModeTun.IsChecked == true ? "tun" : "system";
            _svc.SaveSettings();
            UpdateProxyModeUi();
            UpdateFilterTunHint();
            if (_connected)
                MessageBox.Show(this, "代理模式已修改，将在重新连接后生效。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateProxyModeUi()
        {
            bool tun = string.Equals(_svc.Settings.ProxyMode, "tun", StringComparison.OrdinalIgnoreCase);
            bool admin = AppServices.IsAdministrator();
            if (_tunAdminHint != null)
                _tunAdminHint.Visibility = (tun && !admin) ? Visibility.Visible : Visibility.Collapsed;
            if (_restartAdminBtn != null)
                _restartAdminBtn.Visibility = (tun && !admin) ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>以管理员身份重启自身，成功后退出当前实例</summary>
        private void RestartAsAdministrator()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Verb = "runas",
                    UseShellExecute = true
                });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "提权重启失败：" + ex.Message, "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private UIElement BuildAboutPage()
        {
            var panel = new StackPanel { MaxWidth = 520 };
            panel.Children.Add(MakeSectionTitle("关于 云梯pro 电脑版"));
            panel.Children.Add(new TextBlock
            {
                Text = "版本: v" + AppServices.Version + "\n" +
                       "核心: Xray-core v1.8.3（系统代理模式）+ sing-box v1.3.6 + wintun 0.14.1（TUN 模式）\n" +
                       "      均为 Go ≤1.20 编译，兼容 Windows 7 SP1 及以上\n" +
                       "模式: 系统代理（SOCKS5 + HTTP 本地入站）/ TUN（虚拟网卡接管，支持按应用白/黑名单分流）\n" +
                       "支持协议: vmess / vless / trojan / shadowsocks / socks / http",
                Foreground = CText,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22
            });
            panel.Children.Add(MakeSectionTitle("免责协议"));
            panel.Children.Add(new TextBlock
            {
                Text = DisclaimerText,
                Foreground = CTextSec,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20
            });
            return panel;
        }

        internal const string DisclaimerText =
            "本软件为网络代理工具，仅供学习、研究及合法网络访问使用。\n\n" +
            "一、用户须遵守所在国家或地区的法律法规，严禁将本软件用于任何违法违规用途。\n\n" +
            "二、本软件不内置、不提供任何服务器节点，用户自行配置节点并对其来源与合法性负责。\n\n" +
            "三、使用本软件产生的一切行为及后果由用户自行承担，与本软件作者无关；作者不承担任何直接或间接责任。\n\n" +
            "四、继续使用本软件即表示已阅读并同意以上全部条款。";

        private bool ShowDisclaimerConsent()
        {
            var result = MessageBox.Show(this, DisclaimerText, "免责协议",
                MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (result == MessageBoxResult.OK)
            {
                _svc.Settings.DisclaimerAccepted = true;
                _svc.SaveSettings();
                return true;
            }
            return false;
        }

        /// <summary>从 exe 内嵌图标取窗口图标（单文件分发时无需额外资源文件）</summary>
        private void LoadWindowIcon()
        {
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                {
                    if (ico != null)
                        Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            ico.Handle, Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { }
        }

        private TextBlock MakeSectionTitle(string text)
        {
            return new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = CText, Margin = new Thickness(0, 0, 0, 10) };
        }

        private Button MakeSmallButton(string text, Brush bg, Brush fg)
        {
            return new Button
            {
                Content = text,
                Background = bg,
                Foreground = fg,
                Padding = new Thickness(12, 5, 12, 5),
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
        }

        // ================= 分享 / 编辑 / 导入对话框 =================

        /// <summary>二选一弹窗：返回 0/1，直接关闭返回 -1</summary>
        private int ShowChoiceDialog(string title, string optionA, string optionB)
        {
            int choice = -1;
            var dlg = new Window
            {
                Title = title,
                Owner = this,
                Width = 320,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI")
            };
            var panel = new StackPanel { Margin = new Thickness(16) };
            var btnA = new Button { Content = optionA, Height = 34, Margin = new Thickness(0, 0, 0, 8) };
            btnA.Click += (s, e) => { choice = 0; dlg.DialogResult = true; };
            var btnB = new Button { Content = optionB, Height = 34 };
            btnB.Click += (s, e) => { choice = 1; dlg.DialogResult = true; };
            panel.Children.Add(btnA);
            panel.Children.Add(btnB);
            dlg.Content = panel;
            dlg.ShowDialog();
            return choice;
        }

        /// <summary>导入入口：先选择粘贴链接或识别二维码图片</summary>
        private void ShowImportEntry()
        {
            int c = ShowChoiceDialog("导入链接", "导入节点链接参数", "识别二维码图片");
            if (c == 0) ShowImportDialog();
            else if (c == 1) ImportFromQrImage();
        }

        /// <summary>分享入口：先选择链接参数或二维码</summary>
        private void ShowShareEntry(NodeConfig node)
        {
            int c = ShowChoiceDialog("分享节点「" + node.Name + "」", "分享节点参数", "分享节点二维码");
            if (c == 0) ShowShareDialog(node);
            else if (c == 1) ShowQrShareDialog(node);
        }

        /// <summary>识别二维码图片并导入节点</summary>
        private void ImportFromQrImage()
        {
            var ofd = new OpenFileDialog
            {
                Title = "选择二维码图片",
                Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                CheckFileExists = true,
                Multiselect = false
            };
            if (ofd.ShowDialog(this) != true) return;

            string text = null;
            try
            {
                using (var bmp = new DrawingBitmap(ofd.FileName))
                {
                    text = QrCodeHelper.DecodeFromImage(bmp);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法读取图片：" + ex.Message, "识别失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ParseResult result = null;
            if (!string.IsNullOrWhiteSpace(text))
                result = ShareLinkParser.ParseBatch(text);

            if (result == null || result.Nodes.Count == 0)
            {
                MessageBox.Show(this, "未在图片中识别到节点二维码", "识别结果", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var n in result.Nodes) _svc.UpsertNode(n);
            RefreshNodeList();
            string msg = "已添加节点 " + string.Join("、", result.Nodes.Select(n => n.Name));
            if (result.Errors.Count > 0)
                msg += "\n\n部分内容解析失败：\n" + string.Join("\n", result.Errors.Take(8));
            MessageBox.Show(this, msg, "导入结果", MessageBoxButton.OK,
                result.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        /// <summary>分享节点二维码弹窗</summary>
        private void ShowQrShareDialog(NodeConfig node)
        {
            string url;
            try { url = ShareLinkExporter.Export(node); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法分享", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            DrawingBitmap qrBmp;
            try { qrBmp = QrCodeHelper.GenerateQrBitmap(url); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "生成二维码失败：" + ex.Message, "无法分享", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new Window
            {
                Title = "扫码导入「" + node.Name + "」",
                Owner = this,
                Width = 360,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI")
            };
            var panel = new StackPanel { Margin = new Thickness(16) };
            var image = new Image
            {
                Width = 300,
                Height = 300,
                HorizontalAlignment = HorizontalAlignment.Center,
                Source = ToBitmapSource(qrBmp)
            };
            // 最近邻缩放保持码块边缘锐利
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            panel.Children.Add(image);

            var closeBtn = new Button
            {
                Content = "关闭",
                Width = 80,
                IsCancel = true,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            panel.Children.Add(closeBtn);
            dlg.Content = panel;
            // BitmapSource 已冻结副本，窗口关闭后释放 GDI 位图
            dlg.Closed += (s, e) => qrBmp.Dispose();
            dlg.ShowDialog();
        }

        /// <summary>GDI 位图转 WPF 图像源（冻结后与源位图无关）</summary>
        private static BitmapSource ToBitmapSource(DrawingBitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }

        private void ShowShareDialog(NodeConfig node)
        {
            string url;
            try { url = ShareLinkExporter.Export(node); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法分享", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var dlg = new Window
            {
                Title = "分享节点「" + node.Name + "」",
                Owner = this,
                Width = 560,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI")
            };
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = "其他设备打开「导入链接」，粘贴以下内容即可使用：",
                Foreground = CTextSec, FontSize = 12, Margin = new Thickness(0, 0, 0, 8)
            });

            var box = new TextBox
            {
                Text = url,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 180,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC))
            };
            Grid.SetRow(box, 1);
            grid.Children.Add(box);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var copyBtn = new Button { Content = "复制链接", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            copyBtn.Click += (s, e) => { try { Clipboard.SetText(url); dlg.DialogResult = true; } catch { } };
            var closeBtn = new Button { Content = "关闭", Width = 80, IsCancel = true };
            btnRow.Children.Add(copyBtn);
            btnRow.Children.Add(closeBtn);
            Grid.SetRow(btnRow, 2);
            grid.Children.Add(btnRow);

            dlg.Content = grid;
            dlg.ShowDialog();
        }

        private void ShowEditDialog(NodeConfig existing)
        {
            var dlg = new NodeEditDialog(existing, this);
            if (dlg.ShowDialog() == true)
            {
                var node = dlg.ResultNode;
                var err = node.Validate();
                if (err != null)
                {
                    MessageBox.Show(err, "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _svc.UpsertNode(node);
                RefreshNodeList();
            }
        }

        private void ShowImportDialog()
        {
            var dlg = new ImportDialog(this);
            if (dlg.ShowDialog() == true)
            {
                var result = ShareLinkParser.ParseBatch(dlg.ImportText);
                foreach (var n in result.Nodes) _svc.UpsertNode(n);
                RefreshNodeList();
                string msg = "成功导入 " + result.Nodes.Count + " 个节点。";
                if (result.Errors.Count > 0)
                    msg += "\n\n以下行导入失败：\n" + string.Join("\n", result.Errors.Take(8));
                MessageBox.Show(msg, "导入结果", MessageBoxButton.OK,
                    result.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
        }
    }

    /// <summary>节点编辑对话框（纯代码）</summary>
    public class NodeEditDialog : Window
    {
        public NodeConfig ResultNode;
        private readonly NodeConfig _editing;
        private readonly Dictionary<string, Control> _fields = new Dictionary<string, Control>();
        private ComboBox _protocolCombo;

        public NodeEditDialog(NodeConfig editing, Window owner)
        {
            _editing = editing;
            Title = editing == null ? "新增节点" : "编辑节点";
            Owner = owner;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.White;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");

            var panel = new StackPanel { Margin = new Thickness(20) };

            _protocolCombo = AddCombo(panel, "协议", new[] { "vmess", "vless", "trojan", "shadowsocks", "socks", "http", "dokodemo-door" });
            AddText(panel, "名称", "Name");
            AddText(panel, "服务器地址", "Address");
            AddText(panel, "端口", "Port");
            AddText(panel, "UUID (vmess/vless)", "Uuid");
            AddText(panel, "密码 (trojan/ss/socks/http)", "Password");
            AddText(panel, "用户名 (socks/http)", "Username");
            AddCombo2(panel, "加密方法 (ss)", "Method", new[] { "aes-128-gcm", "aes-256-gcm", "chacha20-ietf-poly1305", "chacha20-ietf", "aes-128-cfb", "aes-256-cfb", "none" });
            AddCombo2(panel, "网络", "Network", new[] { "tcp", "ws" });
            AddCombo2(panel, "TLS", "Tls", new[] { "none", "tls" });
            AddText(panel, "SNI", "Sni");
            AddText(panel, "WS Path", "WsPath");
            AddText(panel, "WS Host", "WsHost");
            AddText(panel, "目标地址 (dokodemo)", "TargetAddress");
            AddText(panel, "目标端口 (dokodemo)", "TargetPort");

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = new Button { Content = "保存", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            ok.Click += (s, e) => { Collect(); DialogResult = true; };
            var cancel = new Button { Content = "取消", Width = 80, IsCancel = true };
            btnRow.Children.Add(ok);
            btnRow.Children.Add(cancel);
            panel.Children.Add(btnRow);

            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560 };

            if (editing != null) Fill(editing);
            else _protocolCombo.SelectedIndex = 0;
        }

        private ComboBox AddCombo(StackPanel p, string label, string[] items)
        {
            p.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 2) });
            var c = new ComboBox { ItemsSource = items };
            p.Children.Add(c);
            return c;
        }

        private void AddCombo2(StackPanel p, string label, string key, string[] items)
        {
            p.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 2) });
            var c = new ComboBox { ItemsSource = items, IsEditable = true };
            p.Children.Add(c);
            _fields[key] = c;
        }

        private void AddText(StackPanel p, string label, string key)
        {
            p.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 2) });
            var t = new TextBox();
            p.Children.Add(t);
            _fields[key] = t;
        }

        private void Fill(NodeConfig n)
        {
            _protocolCombo.SelectedItem = n.Protocol;
            Set("Name", n.Name); Set("Address", n.Address); Set("Port", n.Port.ToString());
            Set("Uuid", n.Uuid); Set("Password", n.Password); Set("Username", n.Username);
            Set("Method", n.Method); Set("Network", n.Network); Set("Tls", n.Tls);
            Set("Sni", n.Sni); Set("WsPath", n.WsPath); Set("WsHost", n.WsHost);
            Set("TargetAddress", n.TargetAddress); Set("TargetPort", n.TargetPort.ToString());
        }

        private void Set(string key, string val)
        {
            Control c;
            if (!_fields.TryGetValue(key, out c)) return;
            var tb = c as TextBox;
            if (tb != null) { tb.Text = val ?? ""; return; }
            var cb = c as ComboBox;
            if (cb != null) cb.Text = val ?? "";
        }

        private string Get(string key)
        {
            Control c;
            if (!_fields.TryGetValue(key, out c)) return "";
            var tb = c as TextBox;
            if (tb != null) return tb.Text.Trim();
            var cb = c as ComboBox;
            return cb != null ? (cb.Text ?? "").Trim() : "";
        }

        private void Collect()
        {
            var n = _editing != null ? _editing.Clone() : new NodeConfig();
            n.Protocol = (_protocolCombo.SelectedItem ?? "vmess").ToString();
            n.Name = Get("Name");
            if (string.IsNullOrWhiteSpace(n.Name)) n.Name = n.Protocol + "-节点";
            n.Address = Get("Address");
            int.TryParse(Get("Port"), out n.Port);
            n.Uuid = Get("Uuid");
            n.Password = Get("Password");
            n.Username = Get("Username");
            n.Method = Get("Method");
            n.Network = Get("Network");
            n.Tls = Get("Tls");
            n.Sni = Get("Sni");
            n.WsPath = Get("WsPath");
            n.WsHost = Get("WsHost");
            n.TargetAddress = Get("TargetAddress");
            int.TryParse(Get("TargetPort"), out n.TargetPort);
            ResultNode = n;
        }
    }

    /// <summary>批量导入对话框</summary>
    public class ImportDialog : Window
    {
        public string ImportText;
        public ImportDialog(Window owner)
        {
            Title = "导入分享链接";
            Owner = owner;
            Width = 520;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = "每行一条链接，支持 vmess:// vless:// trojan:// ss:// socks:// http://",
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var box = new TextBox { AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Name = "Box", FontFamily = new FontFamily("Consolas") };
            Grid.SetRow(box, 1);
            grid.Children.Add(box);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "导入", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            ok.Click += (s, e) => { ImportText = box.Text; DialogResult = true; };
            var cancel = new Button { Content = "取消", Width = 80, IsCancel = true };
            btnRow.Children.Add(ok);
            btnRow.Children.Add(cancel);
            Grid.SetRow(btnRow, 2);
            grid.Children.Add(btnRow);

            Content = grid;
        }
    }
}
