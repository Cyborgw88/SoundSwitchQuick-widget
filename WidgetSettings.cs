using System.Text.Json;

namespace SoundSwitchQuick;

public sealed class WidgetSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Topmost { get; set; } = true;
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
            return JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(SettingsPath)) ?? new WidgetSettings();
        }
        catch { return new WidgetSettings(); }
    }

    public static void Save(WidgetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
