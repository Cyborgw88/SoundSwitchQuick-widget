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
        if (Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) && !DisplayRouter.IsInfrastructure(target))
        {
            router.Choose(target, rule);
            // Let Windows preserve shortcut arguments, working directory and elevation settings.
            Process.Start(new ProcessStartInfo(input) { UseShellExecute = true });
        }
        else
        {
            Process.Start(new ProcessStartInfo(input) { UseShellExecute = true });
            ShowWindowPicker(router, rule);
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
    public static void ShowWindowPicker(DisplayRouter router, DisplayRule? preset = null)
    {
        using var form = new Forms.Form { Text = "Выберите запущенную игру / приложение", Width = 720, Height = 400, StartPosition = Forms.FormStartPosition.CenterScreen };
        var list = new Forms.ListBox { Dock = Forms.DockStyle.Fill, HorizontalScrollbar = true };
        var panel = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Bottom, Height = 100 };
        var screens = new Forms.ComboBox { Width = 420, DropDownStyle = Forms.ComboBoxStyle.DropDownList };
        screens.Items.AddRange(DisplayNative.Screens().Cast<object>().ToArray());
        if (screens.Items.Count > 0) screens.SelectedIndex = 0;
        foreach (DisplayTarget s in screens.Items) if (s.Id == preset?.ScreenId) screens.SelectedItem = s;
        var full = new Forms.CheckBox { Text = "Без рамок на весь экран", Width = 240, Checked = preset?.Borderless ?? true };
        var refresh = new Forms.Button { Text = "Обновить", AutoSize = true };
        var apply = new Forms.Button { Text = "Применить и запомнить", AutoSize = true };
        void Refresh()
        {
            list.Items.Clear();
            var paths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DisplayNative.EnumWindows((h, _) => { var path = DisplayNative.Eligible(h) ? DisplayNative.Executable(h) : null; if (path != null && paths.Add(path)) list.Items.Add(path); return true; }, IntPtr.Zero);
        }
        refresh.Click += (_, _) => Refresh();
        apply.Click += (_, _) =>
        {
            if (list.SelectedItem is not string path || screens.SelectedItem is not DisplayTarget screen) return;
            try { router.Choose(path, new() { ScreenId = screen.Id, Borderless = full.Checked }); form.Close(); }
            catch (Exception ex) { Forms.MessageBox.Show(ex.Message); }
        };
        panel.Controls.AddRange(new Forms.Control[] { screens, full, refresh, apply });
        form.Controls.Add(list); form.Controls.Add(panel); Refresh(); form.ShowDialog();
    }
    public static void ShowSettings(DisplayRouter router)
    {
        using var form = new Forms.Form { Text = "SoundSwitchQuick — Экраны", Width = 650, Height = 460, StartPosition = Forms.FormStartPosition.CenterScreen };
        var panel = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Fill, FlowDirection = Forms.FlowDirection.TopDown, Padding = new Forms.Padding(16), WrapContents = false };
        var enabled = new Forms.CheckBox { Text = "Размещать новые окна на запомненном экране", Width = 590, Checked = router.Settings.Enabled };
        var label = new Forms.Label { Text = "Монитор для первого запуска и при отключённом ТВ:", AutoSize = true };
        var screens = new Forms.ComboBox { Width = 590, DropDownStyle = Forms.ComboBoxStyle.DropDownList };
        screens.Items.AddRange(DisplayNative.Screens().Cast<object>().ToArray());
        foreach (DisplayTarget s in screens.Items) if (s.Id == router.Settings.DefaultScreenId || (router.Settings.DefaultScreenId == "" && s.Primary)) screens.SelectedItem = s;
        if (screens.SelectedIndex < 0 && screens.Items.Count > 0) screens.SelectedIndex = 0;
        var menu = new Forms.CheckBox { Text = "Добавить «Открыть на экране» в меню EXE и ярлыков", Width = 590, Checked = router.Settings.ContextMenuEnabled };
        var help = new Forms.Label { Width = 590, Height = 80, Text = "Windows 11: команда доступна через «Показать дополнительные параметры» или Shift + ПКМ.\nДля обычного двойного клика виджет должен работать в фоне.\nИгры должны поддерживать оконный режим. Звук не переключается." };
        var choose = new Forms.Button { Text = "Выбрать экран для запущенной игры / приложения…", AutoSize = true };
        choose.Click += (_, _) => ShowWindowPicker(router);
        var reset = new Forms.Button { Text = "Сбросить запомненный экран приложения…", AutoSize = true };
        reset.Click += (_, _) =>
        {
            using var dialog = new Forms.Form { Text = "Сбросить правило", Width = 650, Height = 350 };
            var list = new Forms.ListBox { Dock = Forms.DockStyle.Fill, HorizontalScrollbar = true };
            list.Items.AddRange(router.Settings.Applications.Keys.Cast<object>().ToArray());
            var remove = new Forms.Button { Text = "Сбросить выбранное", Dock = Forms.DockStyle.Bottom };
            remove.Click += (_, _) => { if (list.SelectedItem is string path) { router.Settings.Applications.Remove(path); router.Settings.Save(); list.Items.Remove(path); } };
            dialog.Controls.Add(list); dialog.Controls.Add(remove); dialog.ShowDialog(form);
        };
        var save = new Forms.Button { Text = "Сохранить", AutoSize = true };
        save.Click += (_, _) =>
        {
            try
            {
                if (menu.Checked) Register(); else Unregister();
                router.Settings.Enabled = enabled.Checked;
                router.Settings.ContextMenuEnabled = menu.Checked;
                if (screens.SelectedItem is DisplayTarget screen) router.Settings.DefaultScreenId = screen.Id;
                router.Settings.Save(); form.Close();
            }
            catch (Exception ex) { Forms.MessageBox.Show("Не удалось сохранить: " + ex.Message); }
        };
        panel.Controls.AddRange(new Forms.Control[] { enabled, label, screens, menu, help, choose, reset, save });
        form.Controls.Add(panel); form.ShowDialog();
    }
}
