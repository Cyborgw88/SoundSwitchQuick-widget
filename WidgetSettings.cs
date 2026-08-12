using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SoundSwitchQuick;

public sealed class WidgetSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Topmost { get; set; } = true;
    public bool AutostartEnabled { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public Dictionary<string, string> DeviceAliases { get; set; } = new();
}

public static class WidgetSettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoundSwitchQuick");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "widget-settings.json");

    public static WidgetSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new WidgetSettings();

            var settings = JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(SettingsPath))
                           ?? new WidgetSettings();

            settings.DeviceAliases ??= new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(settings.Theme))
                settings.Theme = "Dark";

            return settings;
        }
        catch
        {
            return new WidgetSettings();
        }
    }

    public static void Save(WidgetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings persistence should never prevent the widget from working.
        }
    }
}
