using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace SoundSwitchQuick;

public sealed class TrayService : IDisposable
{
    private readonly MainWindow _window;
    private Forms.NotifyIcon? _icon;

    public TrayService(MainWindow window) => _window = window;

    public void Initialize()
    {
        Icon trayIcon;
        try
        {
            trayIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty)
                       ?? SystemIcons.Application;
        }
        catch
        {
            trayIcon = SystemIcons.Application;
        }

        _icon = new Forms.NotifyIcon
        {
            Text = "SoundSwitch Quick",
            Icon = trayIcon,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                _window.ShowWidgetAndExpand();
        };
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        menu.Items.Add("Показать переключатель", null, (_, _) => _window.ShowWidgetAndExpand());
        menu.Items.Add("Настройки", null, (_, _) => _window.OpenSettings());
        menu.Items.Add("Обновить устройства", null, (_, _) => _window.RefreshDevices());

        var topmostItem = new Forms.ToolStripMenuItem("Поверх остальных окон")
        {
            Checked = _window.Topmost,
            CheckOnClick = false
        };
        topmostItem.Click += (_, _) =>
        {
            _window.ToggleTopmost();
            topmostItem.Checked = _window.Topmost;
        };
        menu.Items.Add(topmostItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => System.Windows.Application.Current.Shutdown());
        return menu;
    }

    public void Dispose()
    {
        if (_icon is null)
            return;

        _icon.Visible = false;
        _icon.Dispose();
    }
}
