using System;
using Microsoft.Win32;

namespace SoundSwitchQuick;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SoundSwitchQuick";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("Не удалось открыть раздел автозапуска Windows.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("Не удалось определить путь к SoundSwitchQuick.exe.");

        key.SetValue(ValueName, $"\"{executablePath}\" --startup", RegistryValueKind.String);
    }
}
