using System;
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

    private bool _isExpandedUp;
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
            RefreshDevices();
        };

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _refreshTimer.Tick += (_, _) => RefreshDevices(false);
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
        if (!IsVisible) Show();
        RefreshDevices();
    }

    public void ShowWidgetAndExpand()
    {
        if (!IsVisible) Show();

        WindowState = WindowState.Normal;
        Activate();
        RefreshDevices(false);
        ExpandWidget();
    }

    public void RefreshDevices() => RefreshDevices(true);

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

        RefreshDevices(false);
        SaveSettings();
    }

    private void RefreshDevices(bool showStatus)
    {
        try
        {
            var devices = _audio.GetPlaybackDevices();
            var current = devices.FirstOrDefault(x => x.IsDefault) ?? devices.FirstOrDefault();

            CurrentName.Text = current is null ? "Нет активного выхода" : GetDisplayName(current);
            CurrentGlyph.Text = current?.Glyph ?? "🔇";

            DeviceButtons.Children.Clear();
            foreach (var device in devices)
                DeviceButtons.Children.Add(CreateDeviceButton(device));

            if (showStatus)
                StatusText.Text = devices.Count == 0
                    ? "Активные устройства не найдены"
                    : $"Доступно устройств: {devices.Count}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось обновить устройства";
            if (showStatus)
                MessageBox.Show(ex.Message, "SoundSwitch Quick", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string GetDisplayName(AudioDeviceItem device)
    {
        if (_settings.DeviceAliases.TryGetValue(device.Id, out var alias) &&
            !string.IsNullOrWhiteSpace(alias))
            return alias.Trim();

        return device.Name;
    }

    private WpfButton CreateDeviceButton(AudioDeviceItem device)
    {
        var displayName = GetDisplayName(device);

        var title = new TextBlock
        {
            Text = displayName,
            Foreground = ThemeService.Brush("TextBrush"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var subtitleText = device.IsDefault
            ? "Сейчас используется"
            : displayName == device.Name
                ? "Нажми, чтобы переключить"
                : device.Name;

        var subtitle = new TextBlock
        {
            Text = subtitleText,
            Foreground = device.IsDefault
                ? ThemeService.Brush("SuccessBrush")
                : ThemeService.Brush("MutedTextBrush"),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var texts = new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        texts.Children.Add(title);
        texts.Children.Add(subtitle);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock
        {
            Text = device.Glyph,
            FontSize = 19,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(texts);

        if (device.IsDefault)
        {
            var check = new TextBlock
            {
                Text = "✓",
                Foreground = ThemeService.Brush("AccentBrush"),
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(check, 2);
            grid.Children.Add(check);
        }

        var button = new WpfButton
        {
            Tag = device.Id,
            Content = grid,
            Margin = new Thickness(0, 0, 0, 7),
            Padding = new Thickness(10),
            Background = device.IsDefault
                ? ThemeService.Brush("DeviceActiveBrush")
                : ThemeService.Brush("DeviceBrush"),
            BorderBrush = ThemeService.Brush("WidgetBorderBrush"),
            BorderThickness = new Thickness(1),
            Foreground = ThemeService.Brush("TextBrush"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            ToolTip = displayName == device.Name ? device.Name : $"{displayName} · {device.Name}"
        };

        button.Click += DeviceButton_Click;
        return button;
    }

    private async void DeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not string deviceId)
            return;

        try
        {
            _audio.SetDefault(deviceId);
            StatusText.Text = "Переключено";
            await Task.Delay(180);
            RefreshDevices(false);
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

    private void Widget_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
        RefreshDevices(false);
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
