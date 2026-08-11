using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SoundSwitchQuick;

public partial class MainWindow : Window
{
    private readonly AudioService _audio = new();
    private readonly WidgetSettings _settings;
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();
        _settings = WidgetSettingsStore.Load();
        Topmost = _settings.Topmost;

        Loaded += (_, _) =>
        {
            RestorePosition();
            RefreshDevices();
        };

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _refreshTimer.Tick += (_, _) => RefreshDevices(false);
        _refreshTimer.Start();
    }

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
        RefreshDevices();
        ExpandedPanel.Visibility = Visibility.Visible;
    }

    public void RefreshDevices() => RefreshDevices(true);

    private void RefreshDevices(bool showStatus)
    {
        try
        {
            var devices = _audio.GetPlaybackDevices();
            var current = devices.FirstOrDefault(x => x.IsDefault) ?? devices.FirstOrDefault();

            CurrentName.Text = current?.Name ?? "Нет активного выхода";
            CurrentGlyph.Text = current?.Glyph ?? "🔇";

            DeviceButtons.Children.Clear();
            foreach (var device in devices)
                DeviceButtons.Children.Add(CreateDeviceButton(device));

            if (showStatus)
                StatusText.Text = devices.Count == 0 ? "Активные устройства не найдены" : $"Доступно устройств: {devices.Count}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось обновить устройства";
            if (showStatus) MessageBox.Show(ex.Message, "SoundSwitch Quick", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Button CreateDeviceButton(AudioDeviceItem device)
    {
        var title = new TextBlock
        {
            Text = device.Name,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var subtitle = new TextBlock
        {
            Text = device.IsDefault ? "Сейчас используется" : "Нажми, чтобы переключить",
            Foreground = device.IsDefault ? new SolidColorBrush(Color.FromRgb(109, 219, 154)) : new SolidColorBrush(Color.FromRgb(163, 170, 183)),
            FontSize = 10.5
        };
        var texts = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(title);
        texts.Children.Add(subtitle);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock { Text = device.Glyph, FontSize = 19, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(texts);

        if (device.IsDefault)
        {
            var check = new TextBlock { Text = "✓", Foreground = new SolidColorBrush(Color.FromRgb(140, 168, 255)), FontSize = 17, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(check, 2);
            grid.Children.Add(check);
        }

        var button = new Button
        {
            Tag = device.Id,
            Content = grid,
            Margin = new Thickness(0, 0, 0, 7),
            Padding = new Thickness(10),
            Background = new SolidColorBrush(device.IsDefault ? Color.FromRgb(38, 43, 55) : Color.FromRgb(28, 32, 40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            ToolTip = device.Name
        };
        button.Click += DeviceButton_Click;
        return button;
    }

    private async void DeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string deviceId) return;
        try
        {
            _audio.SetDefault(deviceId);
            StatusText.Text = "Переключено";
            await Task.Delay(180);
            RefreshDevices(false);
            ExpandedPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка переключения";
            MessageBox.Show(ex.Message, "Не удалось переключить звук", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Widget_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        RefreshDevices(false);
        ExpandedPanel.Visibility = Visibility.Visible;
    }

    private void ExpandedPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!CollapsedCard.IsMouseOver)
            ExpandedPanel.Visibility = Visibility.Collapsed;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void CollapsedCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
            SaveSettings();
        }
        catch { }
    }

    private void CollapsedCard_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var topmost = new MenuItem { Header = "Поверх остальных окон", IsCheckable = true, IsChecked = Topmost };
        topmost.Click += (_, _) =>
        {
            Topmost = topmost.IsChecked;
            SaveSettings();
        };
        var refresh = new MenuItem { Header = "Обновить устройства" };
        refresh.Click += (_, _) => RefreshDevices();
        var exit = new MenuItem { Header = "Выход" };
        exit.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(topmost);
        menu.Items.Add(refresh);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        menu.IsOpen = true;
    }

    private void RestorePosition()
    {
        var area = SystemParameters.WorkArea;
        if (_settings.Left.HasValue && _settings.Top.HasValue)
        {
            Left = Math.Clamp(_settings.Left.Value, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
            Top = Math.Clamp(_settings.Top.Value, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
        }
        else
        {
            Left = area.Right - ActualWidth - 24;
            Top = area.Bottom - ActualHeight - 24;
        }
    }

    public void SaveSettings()
    {
        WidgetSettingsStore.Save(new WidgetSettings { Left = Left, Top = Top, Topmost = Topmost });
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        _audio.Dispose();
        base.OnClosed(e);
    }
}
