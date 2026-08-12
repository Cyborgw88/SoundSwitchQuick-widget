using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SoundSwitchQuick;

public static class ShortcutService
{
    public static string CreateDesktopShortcut()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("Не удалось определить путь к приложению.");

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, "SoundSwitchQuick.lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("Windows Script Host недоступен.");

        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType)
                    ?? throw new InvalidOperationException("Не удалось создать Windows Shell объект.");

            dynamic dynamicShell = shell;
            shortcut = dynamicShell.CreateShortcut(shortcutPath);
            dynamic dynamicShortcut = shortcut;
            dynamicShortcut.TargetPath = executablePath;
            dynamicShortcut.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            dynamicShortcut.IconLocation = $"{executablePath},0";
            dynamicShortcut.Description = "Быстрое переключение аудиовыхода";
            dynamicShortcut.Save();

            return shortcutPath;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }
}
