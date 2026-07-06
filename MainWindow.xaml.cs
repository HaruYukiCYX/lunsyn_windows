using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace lunsyn;

public partial class MainWindow : Window
{
    private readonly ActivityMonitor _monitor = new();
    private readonly ActivitySyncService _syncService = new();
    private readonly ScreenShareService _shareService = new();
    private readonly ScreenCaptureManager _captureManager = new();
    private readonly System.Timers.Timer _syncTimer = new(2000);
    private bool _isConnected = false;
    private bool _isSharing = false;
    private bool _isPinned = false;
    private const int EdgeSnapThreshold = 20;

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

        // 默认位置：屏幕右侧
        Loaded += (_, _) =>
        {
            var screen = Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            var working = screen.WorkingArea;
            Left = working.Right - Width - 10;
            Top = (working.Height - Height) / 2;
        };

        // 失焦时如果不是置顶状态则隐藏
        Deactivated += (_, _) =>
        {
            if (!_isPinned)
                Hide();
        };
    }

    // ========== 边缘吸附 ==========

    private void Window_LocationChanged(object sender, EventArgs e)
    {
        if (_isPinned) return;

        var screen = Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var working = screen.WorkingArea;

        // 转换为屏幕坐标
        var left = Left;
        var top = Top;
        var right = left + Width;
        var bottom = top + Height;

        bool snapped = false;

        // 左边缘
        if (Math.Abs(left - working.Left) < EdgeSnapThreshold)
        {
            AnimateTo(left, working.Left, top, null);
            snapped = true;
        }
        // 右边缘
        else if (Math.Abs(right - working.Right) < EdgeSnapThreshold)
        {
            AnimateTo(left, working.Right - Width, top, null);
            snapped = true;
        }

        // 顶部
        if (Math.Abs(top - working.Top) < EdgeSnapThreshold)
        {
            AnimateTo(left, null, top, working.Top);
            snapped = true;
        }
        // 底部
        else if (Math.Abs(bottom - working.Bottom) < EdgeSnapThreshold)
        {
            AnimateTo(left, null, top, working.Bottom - Height);
            snapped = true;
        }
    }

    private void AnimateTo(double? fromLeft, double? toLeft, double? fromTop, double? toTop)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(150));

        if (toLeft.HasValue)
        {
            var anim = new DoubleAnimation(toLeft.Value, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(Window.LeftProperty, anim);
        }

        if (toTop.HasValue)
        {
            var anim = new DoubleAnimation(toTop.Value, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(Window.TopProperty, anim);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;
        DragMove();
    }

    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        PinBtn.Content = _isPinned ? "📍" : "📌";
        PinBtn.Foreground = _isPinned
            ? new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6))
            : new SolidColorBrush(Color.FromRgb(0xA5, 0xB4, 0xFC));
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Hide();
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
        if (StatusMap.TryGetValue(app, out var status))
        {
            StatusText.Text = status;
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
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        panel.Children.Add(new TextBlock { Text = icon, FontSize = 12, Width = 22, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
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
        SyncStatusLabel.Text = "连接中...";
        SyncDot.Fill = new SolidColorBrush(Colors.Yellow);
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
                    SyncDot.Fill = new SolidColorBrush(Colors.Yellow);
                    SyncStatusLabel.Text = "连接中...";
                    break;
                default:
                    _isConnected = false;
                    _syncTimer.Stop();
                    SyncDot.Fill = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
                    SyncStatusLabel.Text = "未连接";
                    SyncBtn.Content = "连接";
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
        ScreenDot.Fill = new SolidColorBrush(Colors.Yellow);
        await _shareService.StartAsync();
        ScreenStartBtn.IsEnabled = true;
        ScreenJoinBtn.IsEnabled = true;
    }

    private async void ScreenJoinBtn_Click(object sender, RoutedEventArgs e)
    {
        ScreenStartBtn.IsEnabled = false;
        ScreenJoinBtn.IsEnabled = false;
        ScreenStatusLabel.Text = "连接中...";
        ScreenDot.Fill = new SolidColorBrush(Colors.Yellow);
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
                    ScreenDot.Fill = new SolidColorBrush(Colors.Yellow);
                    ScreenStatusLabel.Text = "连接中...";
                    break;
                default:
                    ScreenDot.Fill = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
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
        if (_isSharing)
            _ = _shareService.SendFrameAsync(data);
    }

    private void OnFrameReceived(byte[] data)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                using var ms = new System.IO.MemoryStream(data);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                RemoteFrameImage.Source = bitmap;
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
            ShareBtn.Content = "✅ 已复制";
            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => ShareBtn.Content = orig);
            });
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
