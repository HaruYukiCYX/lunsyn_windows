using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
    }

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
            StatusText.Text = $"耳朵里住着 {(string.IsNullOrEmpty(state.MusicArtist) ? "歌单" : state.MusicArtist)} 的 {(string.IsNullOrEmpty(state.MusicTitle) ? "音乐" : state.MusicTitle)} 🎧";
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
            MyActivityPanel.Children.Add(CreateRow("📱", "应用", state.ForegroundApp));
        if (!string.IsNullOrEmpty(state.BrowserTitle))
            MyActivityPanel.Children.Add(CreateRow("🌐", "网页", state.BrowserTitle));
        if (state.IsPlaying)
        {
            MyActivityPanel.Children.Add(CreateRow("🎵", "音乐", $"{state.MusicArtist} — {state.MusicTitle}"));
            MyActivityPanel.Children.Add(CreateRow("📻", "播放器", state.MusicApp));
        }

        if (MyActivityPanel.Children.Count == 0)
            MyActivityEmpty.Visibility = Visibility.Visible;
    }

    private static StackPanel CreateRow(string icon, string label, string detail)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(new TextBlock { Text = icon, FontSize = 11, Width = 18, TextAlignment = TextAlignment.Center });
        panel.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), Margin = new Thickness(6, 0, 6, 0) });
        panel.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)), TextTrimming = TextTrimming.CharacterEllipsis });
        return panel;
    }

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
            FriendActivityPanel.Children.Add(CreateRow("📱", "应用", payload.ForegroundApp));
            if (payload.IsPlaying)
                FriendActivityPanel.Children.Add(CreateRow("🎵", "音乐", $"{payload.MusicArtist} — {payload.MusicTitle}"));
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

    // ========== 活动同步连接 ==========

    private async void SyncBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected)
        {
            _syncService.Stop();
            _syncTimer.Stop();
            return;
        }
        SyncBtn.IsEnabled = false;
        SyncStatusLabel.Text = "正在连接...";
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
                    SyncDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                    SyncStatusLabel.Text = "已连接";
                    SyncBtn.Content = "断开";
                    SyncBtn.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xEF, 0x44, 0x44));
                    FriendEmptyText.Text = "好友暂无活动";
                    FriendEmptyText.Visibility = Visibility.Visible;
                    break;
                case ActivitySyncService.ConnectionStateEnum.Connecting:
                    SyncDot.Fill = new SolidColorBrush(Colors.Yellow);
                    SyncStatusLabel.Text = "正在连接...";
                    break;
                default:
                    _isConnected = false;
                    _syncTimer.Stop();
                    SyncDot.Fill = new SolidColorBrush(Colors.Gray);
                    SyncStatusLabel.Text = "未连接";
                    SyncBtn.Content = "连接好友";
                    SyncBtn.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xE9, 0x1E, 0x8C));
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
        ScreenStatusLabel.Text = "正在连接...";
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
                    ScreenDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                    ScreenStatusLabel.Text = "已连接";
                    ScreenStartBtn.Visibility = Visibility.Collapsed;
                    ScreenJoinBtn.Visibility = Visibility.Collapsed;
                    ScreenStopBtn.Visibility = Visibility.Visible;
                    // 开始捕获并发送
                    _isSharing = true;
                    await _captureManager.StartCaptureAsync();
                    break;
                case ScreenShareService.ConnectionStateEnum.Connecting:
                    ScreenDot.Fill = new SolidColorBrush(Colors.Yellow);
                    ScreenStatusLabel.Text = "正在连接...";
                    break;
                default:
                    ScreenDot.Fill = new SolidColorBrush(Colors.Gray);
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
            ShareBtn.Content = "✅ 已复制";
            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => ShareBtn.Content = "💬 复制状态到剪贴板");
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
