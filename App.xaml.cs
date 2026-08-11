using System.Windows;

namespace SoundSwitchQuick;

public partial class App : System.Windows.Application
{
    private TrayService? _tray;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mainWindow = new MainWindow();
        _mainWindow.ShowWidget();

        _tray = new TrayService(_mainWindow);
        _tray.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainWindow?.SaveSettings();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
