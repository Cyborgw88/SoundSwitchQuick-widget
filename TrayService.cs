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
        using var iconStream = typeof(TrayService).Assembly.GetManifestResourceStream("SoundSwitchQuick.TrayIcon")
            ?? throw new InvalidOperationException("Ресурс иконки не найден");
        using var sourceIcon = new Icon(iconStream);
        var trayIcon = (Icon)sourceIcon.Clone();

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
        menu.Items.Add("Экраны и запуск приложений", null, (_, _) =>
        {
            if (((App)System.Windows.Application.Current).DisplayRouter is { } router)
                DisplayShell.ShowSettings(router);
        });
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
        var image = _icon.Icon;
        _icon.Dispose();
        image?.Dispose();
    }
}

