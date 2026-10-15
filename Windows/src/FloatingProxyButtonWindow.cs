using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace YunTiPro
{
    /// <summary>桌面右下角置顶悬浮代理开关，复用主窗口的连接逻辑。</summary>
    public sealed class FloatingProxyButtonWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly Border _circle;
        private readonly Border _innerCore;
        private readonly Grid _centerIcon;
        private readonly Border _stopIcon;
        private readonly Image _appIcon;
        private readonly Grid _linkingIcon;
        private readonly TextBlock _stateText;
        private readonly Ellipse _statusDot;
        private readonly DropShadowEffect _glow;
        private Point _pressScreenPoint;
        private double _pressLeft;
        private double _pressTop;
        private bool _pressed;
        private bool _dragging;

        private static readonly Color Cyan = Color.FromRgb(0x22, 0xD3, 0xEE);
        private static readonly Color Amber = Color.FromRgb(0xFB, 0xB8, 0x24);
        private static readonly Color Green = Color.FromRgb(0x34, 0xD3, 0x99);
        private static readonly Color CoreTop = Color.FromRgb(0x0F, 0x23, 0x3D);
        private static readonly Color CoreBottom = Color.FromRgb(0x02, 0x0A, 0x16);

        public FloatingProxyButtonWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

            Title = "云梯pro 代理开关";
            Width = 92;
            Height = 92;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            Focusable = false;

            _stopIcon = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = Brushes.Transparent,
                BorderBrush = Brush(Cyan),
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Border
                {
                    Width = 9,
                    Height = 9,
                    CornerRadius = new CornerRadius(1.5),
                    Background = Brush(Cyan)
                }
            };
            _appIcon = new Image
            {
                Width = 27,
                Height = 27,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Source = LoadApplicationIcon()
            };
            _linkingIcon = new Grid
            {
                Width = 28,
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            for (int i = 0; i < 3; i++)
            {
                _linkingIcon.Children.Add(new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = Brush(Amber),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(i * 11, 0, 0, 0)
                });
            }
            _centerIcon = new Grid
            {
                Width = 30,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 11)
            };
            _centerIcon.Children.Add(_stopIcon);
            _centerIcon.Children.Add(_appIcon);
            _centerIcon.Children.Add(_linkingIcon);
            _stateText = new TextBlock
            {
                Text = "STANDBY",
                Foreground = Brush(Color.FromRgb(0xA5, 0xF3, 0xFC)),
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                FontSize = 7.5,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 11)
            };
            _statusDot = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = Brush(Cyan),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var coreContent = new Grid();
            coreContent.Children.Add(_centerIcon);
            coreContent.Children.Add(_stateText);
            coreContent.Children.Add(_statusDot);

            _innerCore = new Border
            {
                Width = 58,
                Height = 58,
                CornerRadius = new CornerRadius(29),
                Background = Gradient(CoreTop, CoreBottom),
                BorderBrush = Brush(Color.FromArgb(0x75, Cyan.R, Cyan.G, Cyan.B)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = coreContent
            };

            var techFace = new Grid();
            techFace.Children.Add(new Ellipse
            {
                Width = 72,
                Height = 72,
                Stroke = Brush(Color.FromArgb(0x70, Cyan.R, Cyan.G, Cyan.B)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 5, 3 }
            });
            techFace.Children.Add(new Ellipse
            {
                Width = 64,
                Height = 64,
                Stroke = Brush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 0.8
            });
            techFace.Children.Add(_innerCore);

            AddCircuitNode(techFace, HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(4, 0, 0, 0));
            AddCircuitNode(techFace, HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, 4, 0));
            AddCircuitNode(techFace, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, 4, 0, 0));
            AddCircuitNode(techFace, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, 4));

            _glow = new DropShadowEffect
            {
                Color = Cyan,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.58
            };
            _circle = new Border
            {
                Width = 78,
                Height = 78,
                CornerRadius = new CornerRadius(39),
                Background = Brush(Color.FromArgb(0xF2, 0x03, 0x0B, 0x18)),
                BorderBrush = Brush(Cyan),
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.Hand,
                Child = techFace,
                ToolTip = "待机，点击启动代理；按住拖动可移动",
                Effect = _glow
            };
            _circle.MouseLeftButtonDown += OnCircleMouseDown;
            _circle.MouseMove += OnCircleMouseMove;
            _circle.MouseLeftButtonUp += OnCircleMouseUp;
            _circle.LostMouseCapture += OnCircleLostMouseCapture;
            _circle.MouseEnter += (s, e) => { if (!_pressed) { _circle.Opacity = 1; _glow.Opacity = 0.82; } };
            _circle.MouseLeave += (s, e) => { if (!_pressed) { _circle.Opacity = 0.96; _glow.Opacity = 0.58; } };

            Content = new Grid { Children = { _circle } };
            Loaded += (s, e) => MoveToDesktopCorner();
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            _mainWindow.ConnectionStateChanged += OnConnectionStateChanged;
            Closed += (s, e) =>
            {
                SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
                _mainWindow.ConnectionStateChanged -= OnConnectionStateChanged;
            };
        }

        private static SolidColorBrush Brush(Color color)
        {
            return new SolidColorBrush(color);
        }

        private static LinearGradientBrush Gradient(Color top, Color bottom)
        {
            return new LinearGradientBrush(top, bottom, new Point(0.25, 0), new Point(0.75, 1));
        }

        private static ImageSource LoadApplicationIcon()
        {
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                {
                    if (icon == null) return null;
                    return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(32, 32));
                }
            }
            catch
            {
                return null;
            }
        }

        private static void AddCircuitNode(Grid grid, HorizontalAlignment horizontal, VerticalAlignment vertical, Thickness margin)
        {
            grid.Children.Add(new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = Brush(Cyan),
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                Margin = margin
            });
        }

        private void OnCircleMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabled) return;
            _pressed = true;
            _dragging = false;
            _pressScreenPoint = PointToScreen(e.GetPosition(this));
            _pressLeft = Left;
            _pressTop = Top;
            _circle.CaptureMouse();
            _circle.Opacity = 0.84;
            e.Handled = true;
        }

        private void OnCircleMouseMove(object sender, MouseEventArgs e)
        {
            if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
            Point current = PointToScreen(e.GetPosition(this));
            double dx = current.X - _pressScreenPoint.X;
            double dy = current.Y - _pressScreenPoint.Y;
            if (!_dragging && Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance) return;

            _dragging = true;
            _circle.Cursor = Cursors.SizeAll;
            Rect area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, Math.Min(area.Right - Width, _pressLeft + dx));
            Top = Math.Max(area.Top, Math.Min(area.Bottom - Height, _pressTop + dy));
        }

        private async void OnCircleMouseUp(object sender, MouseButtonEventArgs e)
        {
            bool shouldToggle = _pressed && !_dragging;
            EndPointerInteraction();
            e.Handled = true;
            if (!shouldToggle || !IsEnabled) return;
            try
            {
                IsEnabled = false;
                await _mainWindow.ToggleConnectionFromExternalAsync();
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void OnCircleLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_pressed) EndPointerInteraction();
        }

        private void EndPointerInteraction()
        {
            _pressed = false;
            _dragging = false;
            _circle.Cursor = Cursors.Hand;
            _circle.Opacity = 0.96;
            if (_circle.IsMouseCaptured) _circle.ReleaseMouseCapture();
        }

        private void OnConnectionStateChanged(string state)
        {
            bool connecting = state.StartsWith("正在连接", StringComparison.Ordinal) ||
                state.StartsWith("切换节点", StringComparison.Ordinal) ||
                state.IndexOf("验证远端", StringComparison.Ordinal) >= 0;
            bool connected = !connecting && state.StartsWith("已连接", StringComparison.Ordinal);
            Color accent = connecting ? Amber : (connected ? Green : Cyan);

            _circle.BorderBrush = Brush(accent);
            _innerCore.BorderBrush = Brush(Color.FromArgb(0x80, accent.R, accent.G, accent.B));
            _stopIcon.BorderBrush = Brush(accent);
            ((Border)_stopIcon.Child).Background = Brush(accent);
            _stopIcon.Visibility = !connecting && !connected ? Visibility.Visible : Visibility.Collapsed;
            _appIcon.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            _linkingIcon.Visibility = connecting ? Visibility.Visible : Visibility.Collapsed;
            foreach (UIElement child in _linkingIcon.Children)
                ((Ellipse)child).Fill = Brush(accent);
            _statusDot.Fill = Brush(accent);
            _stateText.Foreground = Brush(Color.FromArgb(0xEE, accent.R, accent.G, accent.B));
            _stateText.Text = connecting ? "LINKING" : (connected ? "ONLINE" : "STANDBY");
            _glow.Color = accent;
            _glow.Opacity = connecting ? 0.78 : (connected ? 0.72 : 0.58);
            _circle.ToolTip = connecting
                ? state
                : (connected ? state + "，点击停止代理；按住拖动可移动" : state + "，点击启动代理；按住拖动可移动");
            IsEnabled = !connecting;
            Opacity = connecting ? 0.96 : 1;
        }

        private void OnSystemParametersChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea)) KeepInsideDesktop();
        }

        private void MoveToDesktopCorner()
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Right - Width - 18;
            Top = area.Bottom - Height - 18;
        }

        private void KeepInsideDesktop()
        {
            Rect area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, Math.Min(area.Right - Width, Left));
            Top = Math.Max(area.Top, Math.Min(area.Bottom - Height, Top));
        }
    }
}
