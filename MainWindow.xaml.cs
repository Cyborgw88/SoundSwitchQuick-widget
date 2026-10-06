using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfButton = System.Windows.Controls.Button;

namespace SoundSwitchQuick;

public partial class MainWindow : Window
{
    private readonly AudioService _audio = new();
    private readonly WidgetSettings _settings;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _collapseTimer;

    private IReadOnlyList<AudioDeviceItem> _lastDevices = Array.Empty<AudioDeviceItem>();
    private HashSet<string> _knownDeviceIds = new();
    private bool _deviceSnapshotReady;

    private string? _autoSwitchedToId;
    private string? _returnDeviceId;

    private bool _isExpandedUp;
    private bool _showOtherDevices;
    private double _collapsedAnchorTop;
    private double _collapsedHeight;

    public MainWindow()
    {
        InitializeComponent();

        _settings = WidgetSettingsStore.Load();
        ThemeService.Apply(_settings.Theme);
        Topmost = _settings.Topmost;
        UpdateLayerButton();

        try
        {
            StartupService.Apply(_settings.AutostartEnabled);
        }
        catch
        {
            // The app remains usable even if Windows blocks modifying the Run key.
        }

        Loaded += (_, _) =>
        {
            RestorePosition();
            RefreshDevices(true, false);
        };

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => RefreshDevices(false, true);
        _refreshTimer.Start();

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!RootCard.IsMouseOver)
                CollapseWidget();
        };
    }

    public WidgetSettings Settings => _settings;

    public void ShowWidget()
    {
        if (!IsVisible)
            Show();

        RefreshDevices();
    }

    public void ShowWidgetAndExpand()
    {
        if (!IsVisible)
            Show();

        WindowState = WindowState.Normal;
        Activate();
        RefreshDevices(false, true);
        ExpandWidget();
    }

    public void RefreshDevices() => RefreshDevices(true, true);

    public void OpenSettings()
    {
        CollapseWidget();

        var devices = _audio.GetPlaybackDevices();
        var dialog = new SettingsWindow(this, _settings, devices)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    public void ToggleTopmost()
    {
        Topmost = !Topmost;
        _settings.Topmost = Topmost;
        UpdateLayerButton();
        SaveSettings();
    }

    public void ApplySettingsFromDialog()
    {
        ThemeService.Apply(_settings.Theme);
        Topmost = _settings.Topmost;
        UpdateLayerButton();

        try
        {
            StartupService.Apply(_settings.AutostartEnabled);
            StatusText.Text = _settings.AutostartEnabled
                ? "Автозапуск включён"
                : "Автозапуск выключен";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Автозапуск: {ex.Message}";
        }

        RefreshDevices(false, false);
        SaveSettings();
    }

    private void RefreshDevices(bool showStatus, bool processDeviceChanges)
    {
        try
        {
            var devices = _audio.GetPlaybackDevices();
            var changeMessage = string.Empty;

            if (!_deviceSnapshotReady)
            {
                _knownDeviceIds = new HashSet<string>(devices.Select(x => x.Id));
                _deviceSnapshotReady = true;
            }
            else if (processDeviceChanges)
            {
                var change = ProcessDeviceChanges(devices);
                changeMessage = change.Message ?? string.Empty;

                if (change.DefaultChanged)
                    devices = _audio.GetPlaybackDevices();
            }

            _lastDevices = devices;

            var current = devices.FirstOrDefault(x => x.IsDefault) ?? devices.FirstOrDefault();
            CurrentName.Text = current is null ? "Нет активного выхода" : GetDisplayName(current);
            CurrentGlyph.Text = current?.Glyph ?? "🔇";
            CurrentMeta.Text = current is null
                ? "Нет активного аудиовыхода"
                : $"{(current.IsMuted ? "Без звука" : $"{current.VolumePercent}%")} · колесо = сменить";

            RenderDeviceLists(devices);

            if (!string.IsNullOrWhiteSpace(changeMessage))
                StatusText.Text = changeMessage;
            else if (showStatus)
                StatusText.Text = devices.Count == 0
                    ? "Активные устройства не найдены"
                    : BuildDeviceCountStatus(devices);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось обновить устройства";

            if (showStatus)
                MessageBox.Show(
                    ex.Message,
                    "SoundSwitch Quick",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
        }
    }

    private (bool DefaultChanged, string? Message) ProcessDeviceChanges(IReadOnlyList<AudioDeviceItem> devices)
    {
        var activeIds = new HashSet<string>(devices.Select(x => x.Id));
        var disappeared = _knownDeviceIds.Where(id => !activeIds.Contains(id)).ToList();
        var appearedIds = activeIds.Where(id => !_knownDeviceIds.Contains(id)).ToList();

        _knownDeviceIds = activeIds;

        if (_autoSwitchedToId is not null &&
            disappeared.Contains(_autoSwitchedToId) &&
            _returnDeviceId is not null &&
            activeIds.Contains(_returnDeviceId))
        {
            var returnDevice = devices.FirstOrDefault(x => x.Id == _returnDeviceId);
            var returnName = returnDevice is null ? "предыдущее устройство" : GetDisplayName(returnDevice);

            try
            {
                _audio.SetDefault(_returnDeviceId);
                _autoSwitchedToId = null;
                _returnDeviceId = null;
                return (true, $"Возвращено: {returnName}");
            }
            catch
            {
                _autoSwitchedToId = null;
                _returnDeviceId = null;
            }
        }

        if (appearedIds.Count == 0)
            return (false, null);

        var appeared = devices
            .Where(x => appearedIds.Contains(x.Id))
            .OrderBy(x => GetFavoriteOrder(x.Id))
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var device in appeared)
        {
            var action = GetConnectAction(device.Id);
            if (action == DeviceConnectActions.None)
                continue;

            if (action == DeviceConnectActions.Ask)
            {
                var answer = MessageBox.Show(
                    $"{GetDisplayName(device)} стало доступно. Переключить на него звук?",
                    "SoundSwitchQuick",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (answer != MessageBoxResult.Yes)
                    continue;
            }

            var previous = devices.FirstOrDefault(x => x.IsDefault)?.Id ?? _audio.GetDefaultDeviceId();

            try
            {
                _audio.SetDefault(device.Id);

                if (!string.IsNullOrWhiteSpace(previous) && previous != device.Id)
                {
                    _returnDeviceId = previous;
                    _autoSwitchedToId = device.Id;
                }

                var prefix = action == DeviceConnectActions.Auto ? "Авто" : "Переключено";
                return (true, $"{prefix}: {GetDisplayName(device)}");
            }
            catch (Exception ex)
            {
                return (false, $"Автопереключение: {ex.Message}");
            }
        }

        return (false, null);
    }

    private string GetConnectAction(string deviceId)
    {
        return _settings.DeviceConnectActions.TryGetValue(deviceId, out var action)
            ? action
            : DeviceConnectActions.None;
    }

    private string BuildDeviceCountStatus(IReadOnlyList<AudioDeviceItem> devices)
    {
        var visible = devices.Count(x => !_settings.HiddenDeviceIds.Contains(x.Id));
        var favorites = devices.Count(x =>
            _settings.FavoriteDeviceIds.Contains(x.Id) &&
            !_settings.HiddenDeviceIds.Contains(x.Id));

        return favorites > 0
            ? $"Избранных: {favorites} · доступно: {visible}"
            : $"Доступно устройств: {visible}";
    }

    private string GetDisplayName(AudioDeviceItem device)
    {
        if (_settings.DeviceAliases.TryGetValue(device.Id, out var alias) &&
            !string.IsNullOrWhiteSpace(alias))
            return alias.Trim();

        return device.Name;
    }

    private int GetFavoriteOrder(string deviceId)
    {
        var index = _settings.FavoriteDeviceOrder.IndexOf(deviceId);
        return index < 0 ? int.MaxValue : index;
    }

    private List<AudioDeviceItem> GetFavoriteDevices(IReadOnlyList<AudioDeviceItem> devices)
    {
        return devices
            .Where(x =>
                _settings.FavoriteDeviceIds.Contains(x.Id) &&
                !_settings.HiddenDeviceIds.Contains(x.Id))
            .OrderBy(x => GetFavoriteOrder(x.Id))
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void RenderDeviceLists(IReadOnlyList<AudioDeviceItem> devices)
    {
        DeviceButtons.Children.Clear();
        OtherDeviceButtons.Children.Clear();

        var visible = devices
            .Where(x => !_settings.HiddenDeviceIds.Contains(x.Id))
            .ToList();

        var favorites = GetFavoriteDevices(visible);
        var favoriteIds = new HashSet<string>(favorites.Select(x => x.Id));
        var others = visible
            .Where(x => !favoriteIds.Contains(x.Id))
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (favorites.Count == 0)
        {
            foreach (var device in visible)
                DeviceButtons.Children.Add(CreateDeviceCard(device));

            AllDevicesToggleButton.Visibility = Visibility.Collapsed;
            OtherDeviceButtons.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var device in favorites)
            DeviceButtons.Children.Add(CreateDeviceCard(device));

        if (others.Count == 0)
        {
            AllDevicesToggleButton.Visibility = Visibility.Collapsed;
            OtherDeviceButtons.Visibility = Visibility.Collapsed;
            return;
        }

        AllDevicesToggleButton.Visibility = Visibility.Visible;
        AllDevicesToggleButton.Content = _showOtherDevices
            ? $"Скрыть остальные ({others.Count})"
            : $"Все устройства ({others.Count})";

        OtherDeviceButtons.Visibility = _showOtherDevices
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_showOtherDevices)
        {
            foreach (var device in others)
                OtherDeviceButtons.Children.Add(CreateDeviceCard(device));
        }
    }

    private FrameworkElement CreateDeviceCard(AudioDeviceItem device)
    {
        var displayName = GetDisplayName(device);
        var border = new Border
        {
            Background = device.IsDefault
                ? ThemeService.Brush("DeviceActiveBrush")
                : ThemeService.Brush("DeviceBrush"),
            BorderBrush = ThemeService.Brush("WidgetBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 7)
        };

        var stack = new StackPanel();

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock
        {
            Text = device.Glyph,
            FontSize = 19,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var texts = new StackPanel
        {
            Margin = new Thickness(10, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        texts.Children.Add(new TextBlock
        {
            Text = $"{(_settings.FavoriteDeviceIds.Contains(device.Id) ? "★ " : string.Empty)}{displayName}",
            Foreground = ThemeService.Brush("TextBrush"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        texts.Children.Add(new TextBlock
        {
            Text = device.IsDefault
                ? "Сейчас используется"
                : displayName == device.Name ? device.Name : $"Windows: {device.Name}",
            Foreground = device.IsDefault
                ? ThemeService.Brush("SuccessBrush")
                : ThemeService.Brush("MutedTextBrush"),
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var switchButton = new WpfButton
        {
            Tag = device.Id,
            Content = device.IsDefault ? "✓" : "→",
            Width = 32,
            Height = 30,
            Style = (Style)FindResource("MiniButtonStyle"),
            IsEnabled = !device.IsDefault,
            ToolTip = device.IsDefault ? "Текущий выход" : $"Переключить на {displayName}"
        };
        switchButton.Click += DeviceButton_Click;

        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(switchButton, 2);
        top.Children.Add(glyph);
        top.Children.Add(texts);
        top.Children.Add(switchButton);
        stack.Children.Add(top);

        var volumeRow = new Grid { Margin = new Thickness(3, 9, 0, 0) };
        volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

        var isMuted = device.IsMuted;
        var muteButton = new WpfButton
        {
            Content = isMuted ? "🔇" : "🔊",
            Width = 30,
            Height = 26,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("MiniButtonStyle"),
            ToolTip = "Включить / выключить звук"
        };

        var volumeText = new TextBlock
        {
            Text = $"{device.VolumePercent}%",
            Foreground = ThemeService.Brush("MutedTextBrush"),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = device.VolumePercent,
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Громкость устройства"
        };

        slider.ValueChanged += (_, args) =>
        {
            var percent = Math.Clamp((int)Math.Round(args.NewValue), 0, 100);

            try
            {
                _audio.SetVolume(device.Id, percent);
                volumeText.Text = $"{percent}%";

                if (device.IsDefault)
                    CurrentMeta.Text = $"{(isMuted ? "Без звука" : $"{percent}%")} · колесо = сменить";
            }
            catch
            {
            }
        };

        muteButton.Click += (_, _) =>
        {
            try
            {
                isMuted = _audio.ToggleMute(device.Id);
                muteButton.Content = isMuted ? "🔇" : "🔊";

                if (device.IsDefault)
                    CurrentMeta.Text = $"{(isMuted ? "Без звука" : volumeText.Text)} · колесо = сменить";
            }
            catch
            {
            }
        };

        Grid.SetColumn(muteButton, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(volumeText, 2);
        volumeRow.Children.Add(muteButton);
        volumeRow.Children.Add(slider);
        volumeRow.Children.Add(volumeText);
        stack.Children.Add(volumeRow);

        border.Child = stack;
        border.ToolTip = displayName == device.Name
            ? device.Name
            : $"{displayName} · {device.Name}";

        return border;
    }

    private async void DeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not string deviceId)
            return;

        try
        {
            _autoSwitchedToId = null;
            _returnDeviceId = null;
            _audio.SetDefault(deviceId);
            StatusText.Text = "Переключено";

            await Task.Delay(140);
            RefreshDevices(false, false);
            CollapseWidget();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка переключения";
            MessageBox.Show(
                ex.Message,
                "Не удалось переключить звук",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CollapsedCard_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;

        var devices = _audio.GetPlaybackDevices();
        var favorites = GetFavoriteDevices(devices);

        if (favorites.Count == 0)
        {
            CurrentMeta.Text = "Добавьте избранные устройства в ⚙";
            return;
        }

        if (favorites.Count == 1 && favorites[0].IsDefault)
            return;

        var currentIndex = favorites.FindIndex(x => x.IsDefault);
        var direction = e.Delta < 0 ? 1 : -1;

        var targetIndex = currentIndex < 0
            ? 0
            : (currentIndex + direction + favorites.Count) % favorites.Count;

        try
        {
            _autoSwitchedToId = null;
            _returnDeviceId = null;
            _audio.SetDefault(favorites[targetIndex].Id);
            RefreshDevices(false, false);
        }
        catch
        {
        }
    }

    private void CollapsedCard_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;

        e.Handled = true;

        var current = _audio.GetPlaybackDevices().FirstOrDefault(x => x.IsDefault);
        if (current is null)
            return;

        try
        {
            _audio.ToggleMute(current.Id);
            RefreshDevices(false, false);
        }
        catch
        {
        }
    }

    private void Widget_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
        RefreshDevices(false, true);
        ExpandWidget();
    }

    private void RootCard_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
    }

    private void RootCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void AllDevicesToggle_Click(object sender, RoutedEventArgs e)
    {
        _showOtherDevices = !_showOtherDevices;
        RenderDeviceLists(_lastDevices);

        if (ExpandedPanel.Visibility == Visibility.Visible)
        {
            CollapseWidget();
            ExpandWidget();
        }
    }

    private void ExpandWidget()
    {
        if (ExpandedPanel.Visibility == Visibility.Visible)
            return;

        _collapseTimer.Stop();

        EnsurePanelBelow();

        _collapsedAnchorTop = Top;
        _collapsedHeight = Math.Max(ActualHeight, 62);
        DeviceScrollViewer.MaxHeight = 310;

        ExpandedPanel.Visibility = Visibility.Visible;
        UpdateLayout();

        var workArea = GetCurrentWorkArea();
        var desiredExtraHeight = Math.Max(0, ActualHeight - _collapsedHeight);
        var spaceBelow = Math.Max(0, workArea.Bottom - (_collapsedAnchorTop + _collapsedHeight));
        var spaceAbove = Math.Max(0, _collapsedAnchorTop - workArea.Top);

        _isExpandedUp = spaceBelow < desiredExtraHeight && spaceAbove > spaceBelow;

        var selectedSpace = _isExpandedUp ? spaceAbove : spaceBelow;
        var scrollHeight = DeviceScrollViewer.ActualHeight;
        var fixedExtraHeight = Math.Max(0, desiredExtraHeight - scrollHeight);

        if (selectedSpace < desiredExtraHeight && scrollHeight > 0)
        {
            DeviceScrollViewer.MaxHeight = Math.Max(
                90,
                Math.Min(310, selectedSpace - fixedExtraHeight));
        }

        if (_isExpandedUp)
            EnsurePanelAbove();
        else
            EnsurePanelBelow();

        UpdateLayout();

        var finalExtraHeight = Math.Max(0, ActualHeight - _collapsedHeight);

        if (_isExpandedUp)
            Top = Math.Max(workArea.Top, _collapsedAnchorTop - finalExtraHeight);
        else
            Top = _collapsedAnchorTop;

        ChevronText.Text = _isExpandedUp ? "⌃" : "⌄";
    }

    private void CollapseWidget()
    {
        if (ExpandedPanel.Visibility != Visibility.Visible)
            return;

        ExpandedPanel.Visibility = Visibility.Collapsed;
        EnsurePanelBelow();

        if (_isExpandedUp)
            Top = _collapsedAnchorTop;

        _isExpandedUp = false;
        DeviceScrollViewer.MaxHeight = 310;
        ChevronText.Text = "⌄";
    }

    private void EnsurePanelAbove()
    {
        if (RootStack.Children.IndexOf(ExpandedPanel) == 0)
            return;

        RootStack.Children.Remove(ExpandedPanel);
        RootStack.Children.Insert(0, ExpandedPanel);
        ExpandedPanel.Margin = new Thickness(0, 0, 0, 9);
    }

    private void EnsurePanelBelow()
    {
        if (RootStack.Children.IndexOf(ExpandedPanel) == 1)
        {
            ExpandedPanel.Margin = new Thickness(0, 9, 0, 0);
            return;
        }

        RootStack.Children.Remove(ExpandedPanel);
        RootStack.Children.Add(ExpandedPanel);
        ExpandedPanel.Margin = new Thickness(0, 9, 0, 0);
    }

    private Rect GetCurrentWorkArea()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var screen = Forms.Screen.FromHandle(handle);
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

            var topLeft = transform.Transform(
                new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
            var bottomRight = transform.Transform(
                new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));

            return new Rect(topLeft, bottomRight);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void LayerButton_Click(object sender, RoutedEventArgs e) => ToggleTopmost();

    private void CollapsedCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        try
        {
            CollapseWidget();
            DragMove();
            SaveSettings();
        }
        catch
        {
            // DragMove can throw if the button state changes during the drag.
        }
    }

    private void CollapsedCard_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _collapseTimer.Stop();

        var menu = new ContextMenu();

        var topmost = new MenuItem
        {
            Header = "Поверх остальных окон",
            IsCheckable = true,
            IsChecked = Topmost
        };
        topmost.Click += (_, _) => ToggleTopmost();

        var settings = new MenuItem { Header = "Настройки" };
        settings.Click += (_, _) => OpenSettings();

        var refresh = new MenuItem { Header = "Обновить устройства" };
        refresh.Click += (_, _) => RefreshDevices();

        var exit = new MenuItem { Header = "Выход" };
        exit.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        menu.Items.Add(topmost);
        menu.Items.Add(settings);
        menu.Items.Add(refresh);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        menu.Closed += (_, _) =>
        {
            if (!RootCard.IsMouseOver)
            {
                _collapseTimer.Stop();
                _collapseTimer.Start();
            }
        };

        menu.IsOpen = true;
    }

    private void RestorePosition()
    {
        var area = GetCurrentWorkArea();

        if (_settings.Left.HasValue && _settings.Top.HasValue)
        {
            Left = Math.Clamp(
                _settings.Left.Value,
                area.Left,
                Math.Max(area.Left, area.Right - ActualWidth));

            Top = Math.Clamp(
                _settings.Top.Value,
                area.Top,
                Math.Max(area.Top, area.Bottom - ActualHeight));
        }
        else
        {
            Left = area.Right - ActualWidth - 24;
            Top = area.Bottom - ActualHeight - 24;
        }
    }

    private void UpdateLayerButton()
    {
        LayerButtonGlyph.Text = Topmost ? "📌" : "◌";
        LayerButton.ToolTip = Topmost
            ? "Сейчас поверх всех окон. Нажми для обычного режима"
            : "Сейчас обычный режим. Нажми, чтобы закрепить поверх окон";
    }

    public void SaveSettings()
    {
        _settings.Left = Left;
        _settings.Top = ExpandedPanel.Visibility == Visibility.Visible && _isExpandedUp
            ? _collapsedAnchorTop
            : Top;
        _settings.Topmost = Topmost;
        WidgetSettingsStore.Save(_settings);
    }

    protected override void OnClosed(EventArgs e)
    {
        _collapseTimer.Stop();
        _refreshTimer.Stop();
        _audio.Dispose();
        base.OnClosed(e);
    }
}
