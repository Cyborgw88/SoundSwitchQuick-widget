using System;
using System.Threading;
using System.Windows;

namespace SoundSwitchQuick;

public partial class App : System.Windows.Application
{
    private TrayService? _tray;
    public DisplayRouter? DisplayRouter { get; private set; }
    private DisplayCommandPipe? _displayPipe;
    private MainWindow? _mainWindow;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 1 && e.Args[0] == "--display-self-test")
        {
            try { DisplayRoutingSelfTest.Run(); Shutdown(0); }
            catch (Exception ex) { SoundSwitchQuick.DisplayRouter.Log("SELF-TEST FAILED: " + ex); Shutdown(1); }
            return;
        }
        var createdNew = false;
        _singleInstanceMutex = new Mutex(true, @"Local\SoundSwitchQuick.SingleInstance", out createdNew);

        if (!createdNew)
        {
            if (e.Args.Length > 0)
            {
                try { DisplayCommandPipe.Send(e.Args); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "SoundSwitchQuick"); }
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);
        try
        {
            DisplayRouter = new DisplayRouter();
            _displayPipe = new DisplayCommandPipe(Dispatcher, HandleDisplayCommand);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Настройки экранов недоступны"); }

        _mainWindow = new MainWindow();
        _mainWindow.ShowWidget();

        _tray = new TrayService(_mainWindow);
        _tray.Initialize();
        if (e.Args.Length > 0) HandleDisplayCommand(e.Args);
    }

    private void HandleDisplayCommand(string[] args)
    {
        try
        {
            if (DisplayRouter == null) throw new InvalidOperationException("Модуль экранов недоступен.");
            DisplayShell.Handle(args, DisplayRouter);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Запуск на экране"); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _displayPipe?.Dispose();
        DisplayRouter?.Dispose();
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

