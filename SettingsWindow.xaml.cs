using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SoundSwitchQuick;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;
    private readonly WidgetSettings _settings;
    private readonly IReadOnlyList<AudioDeviceItem> _devices;
    private readonly Dictionary<string, TextBox> _aliasInputs = new();

    public SettingsWindow(
        MainWindow owner,
        WidgetSettings settings,
        IReadOnlyList<AudioDeviceItem> devices)
    {
        InitializeComponent();

        _owner = owner;
        _settings = settings;
        _devices = devices;

        AutostartCheckBox.IsChecked = settings.AutostartEnabled;
        TopmostCheckBox.IsChecked = settings.Topmost;
        DarkThemeRadio.IsChecked = !string.Equals(settings.Theme, ThemeService.Light, StringComparison.OrdinalIgnoreCase);
        LightThemeRadio.IsChecked = string.Equals(settings.Theme, ThemeService.Light, StringComparison.OrdinalIgnoreCase);

        BuildDeviceAliasRows();
    }

    private void BuildDeviceAliasRows()
    {
        DeviceAliasPanel.Children.Clear();
        _aliasInputs.Clear();

        if (_devices.Count == 0)
        {
            DeviceAliasPanel.Children.Add(new TextBlock
            {
                Text = "Сейчас нет активных устройств воспроизведения.",
                Foreground = ThemeService.Brush("MutedTextBrush")
            });
            return;
        }

        foreach (var device in _devices)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

            var info = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };

            info.Children.Add(new TextBlock
            {
                Text = $"{device.Glyph}  {device.Name}",
                Foreground = ThemeService.Brush("TextBrush"),
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            info.Children.Add(new TextBlock
            {
                Text = "Имя Windows",
                Foreground = ThemeService.Brush("FaintTextBrush"),
                FontSize = 9.5
            });

            var input = new TextBox
            {
                ToolTip = "Оставьте пустым, чтобы использовать системное имя Windows"
            };

            if (_settings.DeviceAliases.TryGetValue(device.Id, out var alias))
                input.Text = alias;

            Grid.SetColumn(info, 0);
            Grid.SetColumn(input, 1);
            row.Children.Add(info);
            row.Children.Add(input);

            DeviceAliasPanel.Children.Add(row);
            _aliasInputs[device.Id] = input;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var autostart = AutostartCheckBox.IsChecked == true;

        try
        {
            StartupService.Apply(autostart);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось изменить автозапуск Windows.\n\n{ex.Message}",
                "SoundSwitchQuick",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _settings.AutostartEnabled = autostart;
        _settings.Topmost = TopmostCheckBox.IsChecked == true;
        _settings.Theme = LightThemeRadio.IsChecked == true
            ? ThemeService.Light
            : ThemeService.Dark;

        foreach (var pair in _aliasInputs)
        {
            var alias = pair.Value.Text.Trim();
            if (string.IsNullOrWhiteSpace(alias))
                _settings.DeviceAliases.Remove(pair.Key);
            else
                _settings.DeviceAliases[pair.Key] = alias;
        }

        WidgetSettingsStore.Save(_settings);
        _owner.ApplySettingsFromDialog();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CreateShortcut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = ShortcutService.CreateDesktopShortcut();
            ShortcutStatusText.Text = $"Готово: {path}";
        }
        catch (Exception ex)
        {
            ShortcutStatusText.Text = $"Не удалось создать ярлык: {ex.Message}";
        }
    }
}
