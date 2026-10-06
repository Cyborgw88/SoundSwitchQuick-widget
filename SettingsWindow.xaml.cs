using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SoundSwitchQuick;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;
    private readonly WidgetSettings _settings;
    private readonly IReadOnlyList<AudioDeviceItem> _devices;
    private readonly List<DeviceDraft> _drafts = new();
    private readonly List<string> _favoriteOrder = new();

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

        BuildDrafts();
        BuildDeviceRows();
    }

    private void BuildDrafts()
    {
        _drafts.Clear();
        _favoriteOrder.Clear();
        _favoriteOrder.AddRange(_settings.FavoriteDeviceOrder);

        foreach (var device in _devices)
        {
            _settings.DeviceAliases.TryGetValue(device.Id, out var alias);
            _settings.DeviceConnectActions.TryGetValue(device.Id, out var connectAction);

            _drafts.Add(new DeviceDraft
            {
                Device = device,
                Alias = alias ?? string.Empty,
                Favorite = _settings.FavoriteDeviceIds.Contains(device.Id),
                Hidden = _settings.HiddenDeviceIds.Contains(device.Id),
                ConnectAction = string.IsNullOrWhiteSpace(connectAction)
                    ? DeviceConnectActions.None
                    : connectAction
            });
        }

        foreach (var draft in _drafts.Where(x => x.Favorite))
        {
            if (!_favoriteOrder.Contains(draft.Device.Id))
                _favoriteOrder.Add(draft.Device.Id);
        }
    }

    private IEnumerable<DeviceDraft> OrderedDrafts()
    {
        return _drafts
            .OrderBy(x => x.Favorite ? 0 : 1)
            .ThenBy(x =>
            {
                var index = _favoriteOrder.IndexOf(x.Device.Id);
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(x => x.Device.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private void BuildDeviceRows()
    {
        DeviceAliasPanel.Children.Clear();

        if (_drafts.Count == 0)
        {
            DeviceAliasPanel.Children.Add(new TextBlock
            {
                Text = "Сейчас нет активных устройств воспроизведения.",
                Foreground = ThemeService.Brush("MutedTextBrush")
            });
            return;
        }

        foreach (var draft in OrderedDrafts())
            DeviceAliasPanel.Children.Add(CreateDeviceRow(draft));
    }

    private FrameworkElement CreateDeviceRow(DeviceDraft draft)
    {
        var outer = new Border
        {
            Background = ThemeService.Brush("PanelBrush"),
            BorderBrush = ThemeService.Brush("WidgetBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 9)
        };

        var stack = new StackPanel();

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });

        var info = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0)
        };
        info.Children.Add(new TextBlock
        {
            Text = $"{draft.Device.Glyph}  {draft.Device.Name}",
            Foreground = ThemeService.Brush("TextBrush"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        info.Children.Add(new TextBlock
        {
            Text = $"Сейчас: {draft.Device.VolumePercent}%{(draft.Device.IsMuted ? " · без звука" : string.Empty)}",
            Foreground = ThemeService.Brush("FaintTextBrush"),
            FontSize = 9.5
        });

        var alias = new TextBox
        {
            Text = draft.Alias,
            ToolTip = "Публичное имя. Оставьте пустым, чтобы использовать имя Windows"
        };
        alias.TextChanged += (_, _) => draft.Alias = alias.Text;

        Grid.SetColumn(info, 0);
        Grid.SetColumn(alias, 1);
        top.Children.Add(info);
        top.Children.Add(alias);
        stack.Children.Add(top);

        var controls = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var favorite = new CheckBox
        {
            Content = "⭐ Избранное",
            IsChecked = draft.Favorite,
            Margin = new Thickness(0, 0, 18, 0)
        };

        var hidden = new CheckBox
        {
            Content = "Скрыть",
            IsChecked = draft.Hidden,
            Margin = new Thickness(0, 0, 18, 0)
        };

        var action = new ComboBox
        {
            MinWidth = 205,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Что делать, когда устройство становится доступно"
        };

        AddActionItem(action, "При подключении: ничего", DeviceConnectActions.None);
        AddActionItem(action, "При подключении: спросить", DeviceConnectActions.Ask);
        AddActionItem(action, "При подключении: переключить", DeviceConnectActions.Auto);
        SelectAction(action, draft.ConnectAction);

        var up = new Button
        {
            Content = "↑",
            Width = 32,
            Height = 28,
            Margin = new Thickness(8, 0, 4, 0),
            IsEnabled = draft.Favorite,
            ToolTip = "Поднять в списке избранного"
        };

        var down = new Button
        {
            Content = "↓",
            Width = 32,
            Height = 28,
            IsEnabled = draft.Favorite,
            ToolTip = "Опустить в списке избранного"
        };

        favorite.Checked += (_, _) =>
        {
            draft.Favorite = true;
            draft.Hidden = false;
            if (!_favoriteOrder.Contains(draft.Device.Id))
                _favoriteOrder.Add(draft.Device.Id);
            BuildDeviceRows();
        };

        favorite.Unchecked += (_, _) =>
        {
            draft.Favorite = false;
            _favoriteOrder.Remove(draft.Device.Id);
            BuildDeviceRows();
        };

        hidden.Checked += (_, _) =>
        {
            draft.Hidden = true;
            if (draft.Favorite)
            {
                draft.Favorite = false;
                _favoriteOrder.Remove(draft.Device.Id);
                BuildDeviceRows();
            }
        };

        hidden.Unchecked += (_, _) => draft.Hidden = false;

        action.SelectionChanged += (_, _) =>
        {
            if (action.SelectedItem is ComboBoxItem item && item.Tag is string value)
                draft.ConnectAction = value;
        };

        up.Click += (_, _) => MoveFavorite(draft.Device.Id, -1);
        down.Click += (_, _) => MoveFavorite(draft.Device.Id, 1);

        Grid.SetColumn(favorite, 0);
        Grid.SetColumn(hidden, 1);
        Grid.SetColumn(action, 2);
        Grid.SetColumn(up, 3);
        Grid.SetColumn(down, 4);

        controls.Children.Add(favorite);
        controls.Children.Add(hidden);
        controls.Children.Add(action);
        controls.Children.Add(up);
        controls.Children.Add(down);
        stack.Children.Add(controls);

        outer.Child = stack;
        return outer;
    }

    private static void AddActionItem(ComboBox combo, string title, string value)
    {
        combo.Items.Add(new ComboBoxItem
        {
            Content = title,
            Tag = value
        });
    }

    private static void SelectAction(ComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private void MoveFavorite(string deviceId, int direction)
    {
        var index = _favoriteOrder.IndexOf(deviceId);
        if (index < 0)
            return;

        var newIndex = Math.Clamp(index + direction, 0, _favoriteOrder.Count - 1);
        if (newIndex == index)
            return;

        _favoriteOrder.RemoveAt(index);
        _favoriteOrder.Insert(newIndex, deviceId);
        BuildDeviceRows();
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

        var activeIds = new HashSet<string>(_drafts.Select(x => x.Device.Id));

        foreach (var draft in _drafts)
        {
            var id = draft.Device.Id;
            var alias = draft.Alias.Trim();

            if (string.IsNullOrWhiteSpace(alias))
                _settings.DeviceAliases.Remove(id);
            else
                _settings.DeviceAliases[id] = alias;

            _settings.FavoriteDeviceIds.Remove(id);
            _settings.HiddenDeviceIds.Remove(id);
            _settings.DeviceConnectActions.Remove(id);

            if (draft.Favorite)
                _settings.FavoriteDeviceIds.Add(id);

            if (draft.Hidden)
                _settings.HiddenDeviceIds.Add(id);

            if (!string.Equals(draft.ConnectAction, DeviceConnectActions.None, StringComparison.OrdinalIgnoreCase))
                _settings.DeviceConnectActions[id] = draft.ConnectAction;
        }

        var activeFavoriteOrder = _favoriteOrder
            .Where(id => activeIds.Contains(id) && _settings.FavoriteDeviceIds.Contains(id));

        var inactiveFavoriteOrder = _settings.FavoriteDeviceOrder
            .Where(id => !activeIds.Contains(id) && _settings.FavoriteDeviceIds.Contains(id));

        _settings.FavoriteDeviceOrder = activeFavoriteOrder
            .Concat(inactiveFavoriteOrder)
            .Distinct()
            .ToList();

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

    private sealed class DeviceDraft
    {
        public required AudioDeviceItem Device { get; init; }
        public string Alias { get; set; } = string.Empty;
        public bool Favorite { get; set; }
        public bool Hidden { get; set; }
        public string ConnectAction { get; set; } = DeviceConnectActions.None;
    }
}
