using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SoundSwitchQuick;

public static class DisplayShell
{
    private static readonly string[] Classes = { "exefile", "lnkfile", "InternetShortcut" };
    public static void Register()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Путь приложения неизвестен");
        foreach (var cls in Classes)
        {
            var path = @"Software\Classes\" + cls + @"\shell\SoundSwitchQuick.Display";
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("MUIVerb", "Открыть на экране");
            key.SetValue("SubCommands", "");
            key.SetValue("Icon", "\"" + executable + "\",0");
            key.SetValue("MultiSelectModel", "Single");
            int i = 0;
            foreach (var screen in DisplayNative.Screens())
            {
                foreach (bool borderless in new[] { false, true })
                {
                    using var child = key.CreateSubKey("shell\\" + (++i).ToString("D2"));
                    child.SetValue("MUIVerb", screen.Name + (borderless ? " — весь экран без рамок" : " — обычное окно"));
                    using var command = child.CreateSubKey("command");
                    command.SetValue("", "\"" + executable + "\" --display-launch \"%1\" \"" + screen.Id + "\" " + (borderless ? "borderless" : "window"));
                }
            }
        }
    }
    public static void Unregister()
    {
        foreach (var cls in Classes) Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + cls + @"\shell\SoundSwitchQuick.Display", false);
    }
    public static void Handle(string[] args, DisplayRouter router)
    {
        if (args.Length == 0) return;
        if (args.Length != 4 || args[0] != "--display-launch" || (args[3] != "borderless" && args[3] != "window"))
            throw new ArgumentException("Некорректная команда запуска.");
        var input = Path.GetFullPath(args[1]);
        if (!File.Exists(input)) throw new FileNotFoundException("Файл не найден", input);
        if (!DisplayNative.Screens().Any(s => s.Id == args[2])) throw new InvalidOperationException("Выбранный экран отключён. Обновите меню и выберите доступный экран.");
        var rule = new DisplayRule { ScreenId = args[2], Borderless = args[3] == "borderless" };
        var target = input;
        var extension = Path.GetExtension(input).ToLowerInvariant();
        if (extension == ".lnk") target = ResolveShortcut(input);
        else if (extension != ".exe" && extension != ".url") throw new InvalidOperationException("Поддерживаются EXE, LNK и игровые URL-ярлыки.");
        if (router.Settings.ShortcutApplications.TryGetValue(input, out var mapped) && File.Exists(mapped)) target = mapped;
        if (Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) && !DisplayRouter.IsInfrastructure(target))
        {
            router.Choose(target, rule);
            // Let Windows preserve shortcut arguments, working directory and elevation settings.
            Process.Start(new ProcessStartInfo(input) { UseShellExecute = true });
        }
        else
        {
            Process.Start(new ProcessStartInfo(input) { UseShellExecute = true });
            ShowWindowPicker(router, rule, path =>
            {
                router.Settings.ShortcutApplications[input] = path;
                router.Settings.Save();
            });
        }
    }
    private static string ResolveShortcut(string path)
    {
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host недоступен"));
            shortcut = ((dynamic)shell!).CreateShortcut(path);
            return (string)((dynamic)shortcut).TargetPath;
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
    public static void ShowWindowPicker(DisplayRouter router, DisplayRule? preset = null, Action<string>? onChosen = null)
        => DisplayWindows.ShowPicker(router, preset, onChosen);
    public static void ShowSettings(DisplayRouter router) => DisplayWindows.CreateSettings(router).ShowDialog();
}
