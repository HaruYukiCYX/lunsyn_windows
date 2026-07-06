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
        _popup.Deactivated += (_, _) => _popup.Hide();

        _notifyIcon = new NotifyIcon
        {
            Text = "望月 · Lunsyn",
            Icon = SystemIcons.Application,
            Visible = true
        };

        _notifyIcon.Click += (_, args) =>
        {
            if (args is MouseEventArgs mouseArgs && mouseArgs.Button == MouseButtons.Left)
                TogglePopup();
        };

        _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add("关于望月", null, (_, _) =>
        {
            MessageBox.Show("望月 · Lunsyn\n异地情侣/朋友的桌面伴侣", "关于望月");
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
            var workingArea = screen.WorkingArea;
            _popup.Left = Math.Max(0, Math.Min(Control.MousePosition.X - 170, workingArea.Right - 340));
            _popup.Top = Math.Max(0, workingArea.Bottom - 600);
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
