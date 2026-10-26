using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SoundSwitchQuick;

internal static class DisplayWindows
{
    private static TextBlock Text(string value, double size = 14) => new()
    {
        Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size,
        Foreground = (Brush)Application.Current.FindResource("TextBrush"), Margin = new Thickness(0, 0, 0, 10)
    };
    private static Button Button(string text, Action action)
    {
        var b = new Button { Content = Text(text), HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = (Brush)Application.Current.FindResource("MiniButtonBrush"), BorderBrush = (Brush)Application.Current.FindResource("InputBorderBrush"),
            Padding = new Thickness(14, 10, 14, 0), Margin = new Thickness(0, 0, 0, 10), Cursor = System.Windows.Input.Cursors.Hand };
        b.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "SoundSwitchQuick"); } };
        return b;
    }
    private static CheckBox Check(string text, bool value) => new() { Content = Text(text), IsChecked = value, Margin = new Thickness(0, 4, 0, 12), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private static Window Window(string title)
    {
        var w = new Window { Title = title, Width = Math.Min(720, SystemParameters.WorkArea.Width), Height = Math.Min(760, SystemParameters.WorkArea.Height),
            MinWidth = 380, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = (Brush)Application.Current.FindResource("PanelBrush"), Foreground = (Brush)Application.Current.FindResource("TextBrush"),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 14, Icon = BitmapFrame.Create(new Uri("pack://application:,,,/app.ico")) };
        return w;
    }
    private static ComboBox Screens(string? selected)
    {
        var combo = new ComboBox { ItemsSource = DisplayNative.Screens(), MinHeight = 42, Margin = new Thickness(0, 0, 0, 10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 350 };
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.MarginProperty, new Thickness(4));
        combo.ItemTemplate = new DataTemplate { VisualTree = text };
        combo.SelectedItem = combo.Items.Cast<DisplayTarget>().FirstOrDefault(s => s.Id == selected) ?? combo.Items.Cast<DisplayTarget>().FirstOrDefault(s => s.Primary) ?? combo.Items.Cast<DisplayTarget>().FirstOrDefault();
        return combo;
    }
    private static StackPanel Body(Window w, UIElement footer)
    {
        var root = new DockPanel { LastChildFill = true };
        var bottom = new Border { Padding = new Thickness(24, 12, 24, 8), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = (Brush)Application.Current.FindResource("InputBorderBrush"), Child = footer };
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var body = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        w.Content = root;
        return body;
    }
    private static void Section(Panel parent, string title, params UIElement[] items)
    {
        var body = new StackPanel(); body.Children.Add(Text(title, 18));
        foreach (var item in items) body.Children.Add(item);
        parent.Children.Add(new Border { Background = (Brush)Application.Current.FindResource("CardBrush"), CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 16), Child = body });
    }
    public static Window CreateSettings(DisplayRouter router)
    {
        var w = Window("SoundSwitchQuick · Экраны");
        var enabled = Check("Запоминать экран приложения и размещать на нём новые окна", router.Settings.Enabled);
        var screens = Screens(router.Settings.DefaultScreenId);
        var classic = Check("Показывать команду в классическом меню EXE и ярлыков", router.Settings.ContextMenuEnabled);
        var save = Button("Сохранить настройки", () =>
        {
            if (classic.IsChecked == true) DisplayShell.Register(); else DisplayShell.Unregister();
            router.Settings.Enabled = enabled.IsChecked == true;
            router.Settings.ContextMenuEnabled = classic.IsChecked == true;
            if (screens.SelectedItem is DisplayTarget s) router.Settings.DefaultScreenId = s.Id;
            router.Settings.Save(); w.Close();
        });
        var panel = Body(w, save);
        panel.Children.Add(Text("Приложения — на нужном экране", 24));
        panel.Children.Add(Text("Монитор для работы, телевизор для игр. Выбор сохраняется отдельно для каждого приложения."));
        Section(panel, "Экран по умолчанию", enabled, Text("Здесь откроется приложение при первом запуске или если запомненный телевизор отключён."), screens);
        var status = Text("Первое меню Windows 11 подключается отдельно. Все файлы сборки должны оставаться в одной постоянной папке.");
        var install = Button("Подключить первое меню Windows 11", () => { });
        var uninstall = Button("Удалить расширение первого меню", () => { });
        async Task Run(bool remove)
        {
            install.IsEnabled = uninstall.IsEnabled = false;
            status.Text = remove ? "Удаляем регистрацию расширения…" : "Подключаем расширение… Windows может запросить права администратора.";
            try
            {
                var code = await ModernMenu.Run(remove);
                status.Text = code == 0 ? (remove ? "Расширение удалено. Если пункт ещё виден, выйдите из Windows и войдите снова." : "Расширение зарегистрировано. Откройте новое меню ПКМ. Если пункт не появился, выйдите из Windows и войдите снова.") : "Не удалось подключить расширение. Подробности — в modern-menu.log рядом с EXE.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { install.IsEnabled = uninstall.IsEnabled = true; }
        }
        install.Click += async (_, _) => await Run(false);
        uninstall.Click += async (_, _) => await Run(true);
        Section(panel, "Контекстное меню", Text("Первое меню: ПКМ → «Открыть на экране». Расширение перечисляет подключённые экраны при каждом открытии меню."), install, uninstall, status,
            classic, Text("Классический вариант доступен через «Показать дополнительные параметры» или Shift + ПКМ. Его можно выключить после подключения первого меню."));
        Section(panel, "Сохранённые приложения",
            Button("Выбрать экран для открытой игры или приложения", () => ShowPicker(router)),
            Button("Сбросить сохранённый экран приложения", () => ShowRules(router)));
        Section(panel, "Как это работает", Text("Для обычного двойного клика виджет должен работать в фоне. Новое окно может переместиться через 1–2 секунды. На ТВ можно использовать режим без рамок; игру предварительно переведите в оконный режим. Выбор экрана не меняет устройство звука и громкость."));
        return w;
    }
    public static void ShowPicker(DisplayRouter router, DisplayRule? preset = null, Action<string>? onChosen = null)
    {
        var w = Window("Выбрать приложение и экран");
        var list = new ListBox { MinHeight = 140, MaxHeight = 260, Margin = new Thickness(0, 0, 0, 12) };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        var screens = Screens(preset?.ScreenId);
        var full = Check("Без рамок на весь экран", preset?.Borderless ?? true);
        var selected = Text("Выберите приложение из списка. Полный путь появится здесь.");
        list.SelectionChanged += (_, _) => selected.Text = list.SelectedItem as string ?? "Приложение не выбрано";
        void Refresh()
        {
            var paths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DisplayNative.EnumWindows((h, _) => { var path = DisplayNative.Eligible(h) ? DisplayNative.Executable(h) : null; if (path != null) paths.Add(path); return true; }, IntPtr.Zero);
            list.ItemsSource = paths.OrderBy(p => p).ToArray();
        }
        var body = Body(w, Button("Применить и запомнить", () =>
        {
            if (list.SelectedItem is not string path || screens.SelectedItem is not DisplayTarget target) { MessageBox.Show("Выберите приложение и экран."); return; }
            router.Choose(path, new() { ScreenId = target.Id, Borderless = full.IsChecked == true }); onChosen?.Invoke(path); w.Close();
        }));
        body.Children.Add(Text("Дождитесь игрового окна, затем обновите список. Выберите саму игру, а не Steam или другой лаунчер."));
        body.Children.Add(Button("Обновить список окон", Refresh));
        body.Children.Add(list); body.Children.Add(selected); body.Children.Add(Text("Экран")); body.Children.Add(screens); body.Children.Add(full);
        Refresh(); w.ShowDialog();
    }
    private static void ShowRules(DisplayRouter router)
    {
        var w = Window("Сохранённые приложения");
        var list = new ListBox { MinHeight = 180, MaxHeight = 360 };
        void Refresh() => list.ItemsSource = router.Settings.Applications.Keys.OrderBy(x => x).ToArray();
        var body = Body(w, Button("Сбросить выбранное правило", () => { if (list.SelectedItem is string path) { router.Settings.Applications.Remove(path); router.Settings.Save(); Refresh(); } }));
        body.Children.Add(Text("После сброса новый запуск использует монитор по умолчанию.")); body.Children.Add(list);
        var full = Text(""); list.SelectionChanged += (_, _) => full.Text = list.SelectedItem as string ?? ""; body.Children.Add(full);
        Refresh(); w.ShowDialog();
    }
}

internal static class ModernMenu
{
    public static async Task<int> Run(bool remove)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) throw new InvalidOperationException("Первое меню поддерживается в Windows 11.");
        var directory = Path.GetDirectoryName(Environment.ProcessPath)!;
        var script = Path.Combine(directory, "Install-ModernMenu.ps1");
        if (!File.Exists(script) || !File.Exists(Path.Combine(directory, "SoundSwitchQuick.Shell.dll")))
            throw new FileNotFoundException("Распакуйте всю новую сборку: рядом с EXE должны быть DLL, MSIX и Install-ModernMenu.ps1.");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = directory,
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"" + (remove ? " -Remove" : "") };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить регистрацию.");
        await process.WaitForExitAsync(); return process.ExitCode;
    }
}
