using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace lunsyn;

public partial class MainWindow : Window
{
    // ========== 亚克力毛玻璃效果 ==========

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }
    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
    }

    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    private void EnableAcrylic()
    {
        var windowHelper = new WindowInteropHelper(this);
        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 2,
            GradientColor = 0x00FFFFFF
        };
        var accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = Marshal.SizeOf(accent),
            Data = accentPtr
        };

        SetWindowCompositionAttribute(windowHelper.Handle, ref data);
        Marshal.FreeHGlobal(accentPtr);
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        try { EnableAcrylic(); }
        catch { /* 非 Win10+ 回退 */ }
    }

    // ========== 边缘吸附（支持多屏 + 可拖离） ==========

    private double _dragStartLeft, _dragStartTop;
    private const int SnapThreshold = 25;
    private const int UnsnapOffset = 30; // 解吸附时向内偏移

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;

        // 如果窗口已吸附在边缘，先解吸附
        var screen = GetCurrentScreen();
        var working = screen.WorkingArea;
        if (Math.Abs(Left - working.Left) < 2)
            Left = working.Left + UnsnapOffset;
        else if (Math.Abs(Left + Width - working.Right) < 2)
            Left = working.Right - Width - UnsnapOffset;
        if (Math.Abs(Top - working.Top) < 2)
            Top = working.Top + UnsnapOffset;

        _dragStartLeft = Left;
        _dragStartTop = Top;

        // 使用 WPF 内置 DragMove，不会产生动画锁问题
        DragMove();

        // 拖动结束后，判断是否需要吸附
        if (Math.Abs(Left - _dragStartLeft) < 3 && Math.Abs(Top - _dragStartTop) < 3)
            return;

        SnapToEdge();
    }

    /// 获取窗口当前所在屏幕（支持多屏，包括副屏）
    private Screen GetCurrentScreen()
    {
        var center = new System.Drawing.Point((int)(Left + Width / 2), (int)(Top + Height / 2));
        return Screen.FromPoint(center);
    }

    private void SnapToEdge()
    {
        var screen = GetCurrentScreen();
        var working = screen.WorkingArea;

        if (Math.Abs(Left - working.Left) < SnapThreshold)
            Left = working.Left;
        else if (Math.Abs(Left + Width - working.Right) < SnapThreshold)
            Left = working.Right - Width;

        if (Math.Abs(Top - working.Top) < SnapThreshold)
            Top = working.Top;
    }

    // ========== 核心功能 ==========

    private readonly ActivityMonitor _monitor = new();
    private readonly ActivitySyncService _syncService = new();
    private readonly ScreenShareService _shareService = new();
    private readonly ScreenCaptureManager _captureManager = new();
    private readonly System.Timers.Timer _syncTimer = new(2000);
    private bool _isConnected;
    private bool _isSharing;
    private bool _isPinned;

    private static readonly Dictionary<string, string> StatusMap = new()
    {
        ["Xcode"] = "Indexing... 永远在 Indexing... 🫠",
        ["devenv"] = "Bug 写完了，就差 Debug 了 🐛",
        ["Code"] = "键盘敲得冒火星子了 🔥",
        ["chrome"] = "在互联网的海洋里漂着～ 🌊",
        ["msedge"] = "在互联网的海洋里漂着～ 🌊",
        ["Spotify"] = "耳朵里住着好听的歌 🎧",
        ["steam"] = "游戏库 200+，打开的还是那一个 🎮",
        ["notepad"] = "灵感一闪而过，赶紧记下来 📝"
    };

    public MainWindow()
    {
        InitializeComponent();
        _monitor.MyActivityChanged += OnMyActivityChanged;
        _syncService.ConnectionStateChanged += OnSyncConnectionChanged;
        _syncService.PayloadReceived += OnPayloadReceived;
        _shareService.ConnectionStateChanged += OnShareConnectionChanged;
        _shareService.FrameReceived += OnFrameReceived;
        _captureManager.FrameCaptured += OnFrameCaptured;
        _syncTimer.Elapsed += (_, _) => _ = SendActivityAsync();
        _syncTimer.AutoReset = true;

        Loaded += (_, _) =>
        {
            var screen = Screen.FromHandle(new WindowInteropHelper(this).Handle);
            var working = screen.WorkingArea;
            Left = working.Right - Width - 12;
            Top = (working.Height - Height) / 2 + working.Top;
        };

        Deactivated += (_, _) => { if (!_isPinned) Hide(); };
    }

    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        PinBtn.Content = _isPinned ? "📍" : "📌";
        PinBtn.Foreground = _isPinned
            ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
            : new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Hide();

    // ========== 手动 IP ==========

    private void ManualIPBtn_Click(object sender, RoutedEventArgs e)
    {
        ManualIPPanel.Visibility = Visibility.Visible;
    }

    private void IPCancenBtn_Click(object sender, RoutedEventArgs e)
    {
        ManualIPPanel.Visibility = Visibility.Collapsed;
        IPTextBox.Clear();
    }

    private async void IPConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        var ip = IPTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(ip)) return;

        _syncService.Stop();
        _syncTimer.Stop();
        SyncBtn.IsEnabled = false;
        SyncStatusLabel.Text = "连接中...";
        SyncDot.Fill = new SolidColorBrush(Colors.Gold);
        await _syncService.ConnectAsync(ip);
        SyncBtn.IsEnabled = true;
        ManualIPPanel.Visibility = Visibility.Collapsed;
        IPTextBox.Clear();
    }

    // ========== 活动监控 ==========

    private void OnMyActivityChanged(ActivityState state)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateEmoji(state);
            UpdateStatusText(state);
            UpdateMyActivityPanel(state);
        });
    }

    private void UpdateEmoji(ActivityState state)
    {
        var app = state.ForegroundApp.ToLower();
        if (state.IsPlaying) { EmojiText.Text = "🎵"; return; }
        if (app.Contains("xcode") || app.Contains("code") || app.Contains("devenv") ||
            app.Contains("terminal") || app.Contains("cmd")) { EmojiText.Text = "💻"; return; }
        if (app.Contains("steam") || app.Contains("minecraft") || app.Contains("league")) { EmojiText.Text = "🎮"; return; }
        if (app.Contains("bilibili") || app.Contains("youtube") || app.Contains("netflix")) { EmojiText.Text = "📺"; return; }
        if (app.Contains("wechat") || app.Contains("qq") || app.Contains("discord")) { EmojiText.Text = "💬"; return; }
        if (string.IsNullOrEmpty(app)) { EmojiText.Text = "😴"; return; }
        EmojiText.Text = "🌙";
    }

    private void UpdateStatusText(ActivityState state)
    {
        var app = state.ForegroundApp.ToLower();
        if (state.IsPlaying)
        {
            StatusText.Text = $"{(string.IsNullOrEmpty(state.MusicArtist) ? "歌单" : state.MusicArtist)} — {(string.IsNullOrEmpty(state.MusicTitle) ? "音乐" : state.MusicTitle)} 🎧";
            return;
        }
        if (!string.IsNullOrEmpty(state.BrowserTitle))
        {
            StatusText.Text = "在互联网的海洋里漂着～ 🌊";
            return;
        }
        if (StatusMap.TryGetValue(app, out var s))
        {
            StatusText.Text = s;
            return;
        }
        if (!string.IsNullOrEmpty(app))
        {
            StatusText.Text = $"在 {state.ForegroundApp} 的世界里遨游～";
            return;
        }
        StatusText.Text = "灵魂暂时离线... 勿扰 😴";
    }

    private void UpdateMyActivityPanel(ActivityState state)
    {
        MyActivityPanel.Children.Clear();
        MyActivityEmpty.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrEmpty(state.ForegroundApp))
            MyActivityPanel.Children.Add(CreateRow("📱", state.ForegroundApp));
        if (!string.IsNullOrEmpty(state.BrowserTitle))
            MyActivityPanel.Children.Add(CreateRow("🌐", state.BrowserTitle));
        if (state.IsPlaying)
        {
            MyActivityPanel.Children.Add(CreateRow("🎵", $"{state.MusicArtist} — {state.MusicTitle}"));
            MyActivityPanel.Children.Add(CreateRow("📻", state.MusicApp));
        }

        if (MyActivityPanel.Children.Count == 0)
            MyActivityEmpty.Visibility = Visibility.Visible;
    }

    private static StackPanel CreateRow(string icon, string detail)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 3, 0, 3)
        };
        panel.Children.Add(new TextBlock
        {
            Text = icon, FontSize = 12, Width = 22,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail, FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        });
        return panel;
    }

    // ========== 好友活动 ==========

    private void OnPayloadReceived(ActivityPayload payload)
    {
        Dispatcher.Invoke(() =>
        {
            _monitor.UpdateFriend(payload);
            UpdateFriendPanel(payload);
        });
    }

    private void UpdateFriendPanel(ActivityPayload payload)
    {
        FriendActivityPanel.Children.Clear();
        FriendEmptyText.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrEmpty(payload.ForegroundApp))
        {
            FriendActivityPanel.Children.Add(CreateRow("📱", payload.ForegroundApp));
            if (payload.IsPlaying)
                FriendActivityPanel.Children.Add(CreateRow("🎵", $"{payload.MusicArtist} — {payload.MusicTitle}"));
        }
        else
        {
            FriendEmptyText.Text = "好友暂无活动";
            FriendEmptyText.Visibility = Visibility.Visible;
        }
    }

    private async Task SendActivityAsync()
    {
        if (_isConnected)
            await _syncService.SendPayloadAsync(_monitor.ToPayload());
    }

    private async void SyncBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected)
        {
            _syncService.Stop();
            _syncTimer.Stop();
            return;
        }
        SyncBtn.IsEnabled = false;
        SyncStatusLabel.Text = "自动连接中...";
        SyncDot.Fill = new SolidColorBrush(Colors.Gold);
        await _syncService.StartAsync();
        SyncBtn.IsEnabled = true;
    }

    private void OnSyncConnectionChanged(ActivitySyncService.ConnectionStateEnum state)
    {
        Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case ActivitySyncService.ConnectionStateEnum.Connected:
                    _isConnected = true;
                    _syncTimer.Start();
                    SyncDot.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                    SyncStatusLabel.Text = "已连接";
                    SyncBtn.Content = "断开";
                    SyncBtn.Style = (Style)FindResource("DangerBtn");
                    FriendEmptyText.Text = "好友暂无活动";
                    FriendEmptyText.Visibility = Visibility.Visible;
                    break;
                case ActivitySyncService.ConnectionStateEnum.Connecting:
                    SyncDot.Fill = new SolidColorBrush(Colors.Gold);
                    SyncStatusLabel.Text = "连接中...";
                    break;
                default:
                    _isConnected = false;
                    _syncTimer.Stop();
                    SyncDot.Fill = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                    SyncStatusLabel.Text = "未连接";
                    SyncBtn.Content = "自动连接";
                    SyncBtn.Style = (Style)FindResource("PrimaryBtn");
                    FriendEmptyText.Text = "好友暂未连接";
                    FriendEmptyText.Visibility = Visibility.Visible;
                    break;
            }
        });
    }

    // ========== 屏幕共享 ==========

    private async void ScreenStartBtn_Click(object sender, RoutedEventArgs e)
    {
        ScreenStartBtn.IsEnabled = false;
        ScreenJoinBtn.IsEnabled = false;
        ScreenStatusLabel.Text = "等待连接...";
        ScreenDot.Fill = new SolidColorBrush(Colors.Gold);
        await _shareService.StartAsync();
        ScreenStartBtn.IsEnabled = true;
        ScreenJoinBtn.IsEnabled = true;
    }

    private async void ScreenJoinBtn_Click(object sender, RoutedEventArgs e)
    {
        ScreenStartBtn.IsEnabled = false;
        ScreenJoinBtn.IsEnabled = false;
        ScreenStatusLabel.Text = "连接中...";
        ScreenDot.Fill = new SolidColorBrush(Colors.Gold);
        await _shareService.StartAsync();
        ScreenStartBtn.IsEnabled = true;
        ScreenJoinBtn.IsEnabled = true;
    }

    private void ScreenStopBtn_Click(object sender, RoutedEventArgs e)
    {
        _shareService.Stop();
        _captureManager.StopCapture();
        _isSharing = false;
    }

    private void OnShareConnectionChanged(ScreenShareService.ConnectionStateEnum state)
    {
        Dispatcher.Invoke(async () =>
        {
            switch (state)
            {
                case ScreenShareService.ConnectionStateEnum.Connected:
                    ScreenDot.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                    ScreenStatusLabel.Text = "已连接";
                    ScreenStartBtn.Visibility = Visibility.Collapsed;
                    ScreenJoinBtn.Visibility = Visibility.Collapsed;
                    ScreenStopBtn.Visibility = Visibility.Visible;
                    _isSharing = true;
                    await _captureManager.StartCaptureAsync();
                    break;
                case ScreenShareService.ConnectionStateEnum.Connecting:
                    ScreenDot.Fill = new SolidColorBrush(Colors.Gold);
                    ScreenStatusLabel.Text = "连接中...";
                    break;
                default:
                    ScreenDot.Fill = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                    ScreenStatusLabel.Text = "未连接";
                    ScreenStartBtn.Visibility = Visibility.Visible;
                    ScreenJoinBtn.Visibility = Visibility.Visible;
                    ScreenStopBtn.Visibility = Visibility.Collapsed;
                    RemoteFrameImage.Visibility = Visibility.Collapsed;
                    _captureManager.StopCapture();
                    _isSharing = false;
                    break;
            }
        });
    }

    private void OnFrameCaptured(byte[] data)
    {
        if (_isSharing) _ = _shareService.SendFrameAsync(data);
    }

    private void OnFrameReceived(byte[] data)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                using var ms = new System.IO.MemoryStream(data);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                RemoteFrameImage.Source = bmp;
                RemoteFrameImage.Visibility = Visibility.Visible;
            }
            catch { }
        });
    }

    // ========== 分享 ==========

    private void ShareBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(StatusText.Text);
            var orig = ShareBtn.Content;
            ShareBtn.Content = "✅ 已复制到剪贴板";
            _ = Task.Delay(2000).ContinueWith(_ =>
                Dispatcher.Invoke(() => ShareBtn.Content = orig));
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitor.Dispose();
        _syncService.Dispose();
        _shareService.Dispose();
        _captureManager.Dispose();
        _syncTimer.Dispose();
        base.OnClosed(e);
    }
}