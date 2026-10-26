using System;
using System.Drawing;
using System.Text.Json;

namespace SoundSwitchQuick;

internal static class DisplayRoutingSelfTest
{
    public static void Run()
    {
        var monitor = new DisplayTarget("monitor", "Монитор", new Rectangle(-1920, 0, 1920, 1080), new Rectangle(-1920, 0, 1920, 1040), true);
        var tv = new DisplayTarget("tv", "ТВ", new Rectangle(0, 0, 3840, 2160), new Rectangle(0, 0, 3840, 2120), false);
        var screens = new[] { monitor, tv };
        var initial = DisplayRouter.ResolveTarget(screens, "monitor", new());
        Check(initial.Screen == monitor && !initial.Borderless, "Первый запуск на мониторе");
        var rule = new DisplayRule { ScreenId = "tv", Borderless = true };
        var remembered = DisplayRouter.ResolveTarget(screens, "monitor", rule);
        Check(remembered.Screen == tv && remembered.Borderless, "Запомненный ТВ");
        var disconnected = DisplayRouter.ResolveTarget(new[] { monitor }, "monitor", rule);
        Check(disconnected.Screen == monitor && !disconnected.Borderless && rule.ScreenId == "tv", "Отключённый ТВ без потери правила");
        Check(DisplayRouter.ResolveTarget(screens, "monitor", rule).Borderless, "Возврат ТВ");
        Check(DisplayRouter.ResolveTarget(screens, "missing", new()).Screen == monitor, "Недоступный монитор по умолчанию");
        Check(DisplayRouter.ResolveTarget(Array.Empty<DisplayTarget>(), "monitor", rule).Screen == null, "Нет дисплеев");
        var settings = new DisplayRoutingSettings();
        settings.Applications[@"C:\Games\Game.exe"] = rule;
        Check(settings.Applications.ContainsKey(@"c:\games\GAME.EXE"), "Регистр пути EXE");
        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<DisplayRoutingSettings>(json)!;
        Check(restored.Applications[@"C:\Games\Game.exe"].Borderless, "Сохранение режима");
        Check(DisplayRouter.IsInfrastructure(@"C:\Steam\steam.exe"), "Не перемещать лаунчер");
        Check(!DisplayRouter.IsInfrastructure(@"C:\Windows\System32\notepad.exe"), "Обычные приложения Windows");
        // Exercise native interop on the Windows build agent as well.
        Check(DisplayNative.Screens().Count > 0, "EnumDisplayDevices / DPI context");
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}
