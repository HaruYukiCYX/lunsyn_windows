using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using Timer = System.Timers.Timer;

namespace lunsyn;

public class ActivityState
{
    public string ForegroundApp { get; set; } = "";
    public string BrowserURL { get; set; } = "";
    public string BrowserTitle { get; set; } = "";
    public string MusicApp { get; set; } = "";
    public string MusicTitle { get; set; } = "";
    public string MusicArtist { get; set; } = "";
    public bool IsPlaying { get; set; }
}

public class ActivityMonitor : IDisposable
{
    public event Action<ActivityState>? MyActivityChanged;
    public ActivityState MyActivity { get; private set; } = new();
    public ActivityState FriendActivity { get; private set; } = new();

    private readonly Timer _timer;
    private string _lastForegroundApp = "";

    // Win32 API
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public ActivityMonitor()
    {
        _timer = new Timer(2000);
        _timer.Elapsed += (_, _) => UpdateActivity();
        _timer.AutoReset = true;
        _timer.Start();
    }

    private void UpdateActivity()
    {
        var state = new ActivityState();
        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out uint pid);

        try
        {
            var proc = Process.GetProcessById((int)pid);
            state.ForegroundApp = proc.ProcessName;

            // 浏览器检测
            if (IsBrowser(proc.ProcessName))
            {
                var title = new StringBuilder(256);
                GetWindowText(hwnd, title, 256);
                state.BrowserTitle = title.ToString();
            }

            // 音乐检测
            DetectMusic(state);
        }
        catch { }

        MyActivity = state;
        MyActivityChanged?.Invoke(state);
    }

    private static bool IsBrowser(string name)
    {
        var browsers = new[] { "chrome", "msedge", "firefox", "opera", "brave" };
        return browsers.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase));
    }

    private static void DetectMusic(ActivityState state)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Name FROM Win32_Process WHERE Name LIKE '%music%' OR Name LIKE '%spotify%' OR Name LIKE '%qqmusic%' OR Name = 'netease.exe'"
            );
            foreach (var obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(name))
                {
                    state.MusicApp = name.Replace(".exe", "");
                    state.MusicTitle = "正在播放";
                    state.MusicArtist = "";
                    state.IsPlaying = true;
                    return;
                }
            }
        }
        catch { }
    }

    public ActivityPayload ToPayload()
    {
        var a = MyActivity;
        return new ActivityPayload
        {
            ForegroundApp = a.ForegroundApp,
            BrowserURL = a.BrowserURL,
            BrowserTitle = a.BrowserTitle,
            MusicApp = a.MusicApp,
            MusicTitle = a.MusicTitle,
            MusicArtist = a.MusicArtist,
            IsPlaying = a.IsPlaying
        };
    }

    public void UpdateFriend(ActivityPayload p)
    {
        FriendActivity = new ActivityState
        {
            ForegroundApp = p.ForegroundApp,
            BrowserURL = p.BrowserURL,
            BrowserTitle = p.BrowserTitle,
            MusicApp = p.MusicApp,
            MusicTitle = p.MusicTitle,
            MusicArtist = p.MusicArtist,
            IsPlaying = p.IsPlaying
        };
    }

    public void Dispose() => _timer.Dispose();
}
