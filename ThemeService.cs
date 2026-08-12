using System;
using System.Windows;
using System.Windows.Media;

namespace SoundSwitchQuick;

public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    public static void Apply(string? theme)
    {
        var isLight = string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase);
        var resources = Application.Current.Resources;

        Set(resources, "WidgetBrush", isLight ? "#F7F9FC" : "#F013161C");
        Set(resources, "WidgetBorderBrush", isLight ? "#260D1B31" : "#3AFFFFFF");
        Set(resources, "CardBrush", isLight ? "#FFFFFFFF" : "#FF1C2028");
        Set(resources, "PanelBrush", isLight ? "#FFF2F5FA" : "#FF171B22");
        Set(resources, "DeviceBrush", isLight ? "#FFFFFFFF" : "#FF1C2028");
        Set(resources, "DeviceActiveBrush", isLight ? "#FFE8F0FF" : "#FF262B37");
        Set(resources, "TextBrush", isLight ? "#FF172033" : "#FFF7F8FB");
        Set(resources, "MutedTextBrush", isLight ? "#FF5E6878" : "#FFA3AAB7");
        Set(resources, "FaintTextBrush", isLight ? "#FF7B8492" : "#FF6E7685");
        Set(resources, "AccentBrush", isLight ? "#FF2F6FED" : "#FF8CA8FF");
        Set(resources, "AccentSoftBrush", isLight ? "#1F2F6FED" : "#263F66FF");
        Set(resources, "SuccessBrush", isLight ? "#FF16804B" : "#FF6DDB9A");
        Set(resources, "MiniButtonBrush", isLight ? "#FFE8EDF5" : "#FF252B35");
        Set(resources, "InputBrush", isLight ? "#FFFFFFFF" : "#FF11151B");
        Set(resources, "InputBorderBrush", isLight ? "#330D1B31" : "#33FFFFFF");
    }

    public static SolidColorBrush Brush(string key)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush)
            return brush;

        return new SolidColorBrush(Colors.Transparent);
    }

    private static void Set(ResourceDictionary resources, string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        resources[key] = new SolidColorBrush(color);
    }
}
