using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace lunsyn;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private MainWindow? _popup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _popup = new MainWindow();

        _notifyIcon = new NotifyIcon
        {
            Text = "望月 · Lunsyn",
            Icon = SystemIcons.Application,
            Visible = true
        };

        _notifyIcon.Click += (_, args) =>
        {
            if (args is MouseEventArgs m && m.Button == MouseButtons.Left)
                TogglePopup();
        };

        _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add("关于望月", null, (_, _) =>
        {
            MessageBox.Show(
                "望月 · Lunsyn\n\n" +
                "专为异地情侣/朋友设计的桌面伴侣\n" +
                "实时活动同步 · 屏幕共享\n\n" +
                "macOS / Windows 跨平台互通",
                "关于望月");
        });
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add("退出望月", null, (_, _) =>
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _popup?.Close();
            Shutdown();
        });
    }

    private void TogglePopup()
    {
        if (_popup == null) return;
        if (_popup.IsVisible)
        {
            _popup.Hide();
        }
        else
        {
            var screen = Screen.FromPoint(Control.MousePosition);
            var working = screen.WorkingArea;
            _popup.Left = working.Right - _popup.Width - 12;
            _popup.Top = Math.Max(working.Top, (working.Height - _popup.Height) / 2 + working.Top);
            _popup.Show();
            _popup.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }
}
