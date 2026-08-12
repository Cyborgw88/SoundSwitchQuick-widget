using System;
using System.Threading;
using System.Windows;

namespace SoundSwitchQuick;

public partial class App : System.Windows.Application
{
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        var createdNew = false;
        _singleInstanceMutex = new Mutex(true, @"Local\SoundSwitchQuick.SingleInstance", out createdNew);

        if (!createdNew)
        {
            Shutdown();
            return;
        }

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

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch
        {
            // Process may not own the mutex if startup was interrupted.
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
