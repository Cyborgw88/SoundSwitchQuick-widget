using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SoundSwitchQuick;

public sealed class DisplayRule
{
    public string ScreenId { get; set; } = "";
    public bool Borderless { get; set; }
}

public sealed class DisplayRoutingSettings
{
    public bool Enabled { get; set; } = true;
    public bool ContextMenuEnabled { get; set; }
    public string DefaultScreenId { get; set; } = "";
    public Dictionary<string, DisplayRule> Applications { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundSwitchQuick");
    public static string FilePath => Path.Combine(DirectoryPath, "display-routing.json");
    public static DisplayRoutingSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            var value = JsonSerializer.Deserialize<DisplayRoutingSettings>(File.ReadAllText(FilePath)) ?? new();
            value.Applications = new(value.Applications ?? new(), StringComparer.OrdinalIgnoreCase);
            return value;
        }
        catch (Exception ex)
        {
            DisplayRouter.Log("Не удалось прочитать настройки экранов: " + ex.Message);
            // Never silently overwrite a damaged file with defaults.
            throw new InvalidDataException("Не удалось прочитать display-routing.json. Сохраните копию файла и исправьте его или переименуйте.", ex);
        }
    }
    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}

public sealed record DisplayTarget(string Id, string Name, System.Drawing.Rectangle Bounds,
    System.Drawing.Rectangle WorkingArea, bool Primary)
{
    public override string ToString() => Name + (Primary ? " (основной Windows)" : "");
}

internal static class DisplayNative
{
    public delegate bool EnumCallback(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetStyle(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] public static extern IntPtr SetStyle(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevices(string? device, uint number, ref Device info, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct Device
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    public static List<DisplayTarget> Screens()
    {
        var old = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            return Forms.Screen.AllScreens.Select(s =>
            {
                var d = new Device { Size = Marshal.SizeOf<Device>() };
                var ok = EnumDisplayDevices(s.DeviceName, 0, ref d, 1);
                return new DisplayTarget(ok && !string.IsNullOrWhiteSpace(d.DeviceId) ? d.DeviceId : s.DeviceName,
                    s.DeviceName + " — " + (ok ? d.DeviceString : "Экран"), s.Bounds, s.WorkingArea, s.Primary);
            }).ToList();
        }
        finally { if (old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
    }
    public static string? Executable(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return null;
        try { using var p = Process.GetProcessById((int)pid); return p.MainModule?.FileName; }
        catch { return null; }
    }
    public static bool Eligible(IntPtr h)
    {
        if (!IsWindowVisible(h) || IsIconic(h) || GetWindow(h, 4) != IntPtr.Zero) return false;
        if ((GetStyle(h, -20).ToInt64() & 0x80) != 0) return false; // tool window
        if (DwmGetWindowAttribute(h, 14, out var cloaked, 4) == 0 && cloaked != 0) return false;
        return GetWindowRect(h, out var r) && r.Right-r.Left >= 160 && r.Bottom-r.Top >= 100;
    }
}

public sealed class DisplayRouter : IDisposable
{
    public DisplayRoutingSettings Settings { get; }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Dictionary<IntPtr, (uint Pid, DateTime First, bool Done)> _seen = new();
    private readonly Dictionary<IntPtr, (uint Pid, long Style, DisplayNative.Rect Rect)> _original = new();
    private string _topology = "";
    private DateTime _nextTopologyCheck;
    public DisplayRouter()
    {
        Settings = DisplayRoutingSettings.Load();
        // Existing windows are not moved on startup.
        DisplayNative.EnumWindows((h, _) => { DisplayNative.GetWindowThreadProcessId(h, out var pid); _seen[h] = (pid, DateTime.UtcNow, true); return true; }, IntPtr.Zero);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }
    public static void Log(string text)
    {
        try { Directory.CreateDirectory(DisplayRoutingSettings.DirectoryPath); var path = Path.Combine(DisplayRoutingSettings.DirectoryPath, "display-routing.log"); if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".old", true); File.AppendAllText(path, DateTime.Now.ToString("s") + " " + text + Environment.NewLine); } catch { }
    }
    private void Tick()
    {
        try
        {
            if (DateTime.UtcNow >= _nextTopologyCheck)
            {
                _nextTopologyCheck = DateTime.UtcNow.AddSeconds(3);
                var topology = string.Join("|", DisplayNative.Screens().Select(s => s.Id + s.Bounds));
                if (topology != _topology)
                {
                    _topology = topology;
                    if (Settings.ContextMenuEnabled) DisplayShell.Register();
                }
            }
            var live = new HashSet<IntPtr>();
            DisplayNative.EnumWindows((h, _) =>
            {
                live.Add(h);
                DisplayNative.GetWindowThreadProcessId(h, out var pid);
                if (!_seen.TryGetValue(h, out var seen) || seen.Pid != pid)
                { seen = (pid, DateTime.UtcNow, false); _seen[h] = seen; _original.Remove(h); }
                if (seen.Done || !DisplayNative.Eligible(h)) return true;
                // Allow the application to finish its initial placement, then act only once.
                if ((DateTime.UtcNow - seen.First).TotalMilliseconds < 1200) return true;
                var path = DisplayNative.Executable(h);
                if (path == null) return true;
                _seen[h] = (pid, seen.First, true);
                if (Settings.Enabled && !IsInfrastructure(path))
                {
                    Settings.Applications.TryGetValue(path, out var rule);
                    Place(h, rule ?? new DisplayRule { ScreenId = Settings.DefaultScreenId });
                }
                return true;
            }, IntPtr.Zero);
            foreach (var h in _seen.Keys.Where(h => !live.Contains(h)).ToArray()) { _seen.Remove(h); _original.Remove(h); }
        }
        catch (Exception ex) { Log(ex.Message); }
    }
    internal static bool IsInfrastructure(string path)
    {
        return new[] { "explorer", "shellexperiencehost", "startmenuexperiencehost", "searchhost", "applicationframehost", "lockapp", "textinputhost", "steam", "epicgameslauncher", "battle.net", "eadesktop" }.Contains(Path.GetFileNameWithoutExtension(path).ToLowerInvariant());
    }
    internal static (DisplayTarget? Screen, bool Borderless) ResolveTarget(System.Collections.Generic.IReadOnlyList<DisplayTarget> screens, string defaultId, DisplayRule rule)
    {
        var requested = screens.FirstOrDefault(s => s.Id == rule.ScreenId);
        return (requested ?? screens.FirstOrDefault(s => s.Id == defaultId) ?? screens.FirstOrDefault(s => s.Primary) ?? screens.FirstOrDefault(), requested != null && rule.Borderless);
    }

    public void Choose(string executable, DisplayRule rule)
    {
        Settings.Applications[Path.GetFullPath(executable)] = rule;
        Settings.Save();
        DisplayNative.EnumWindows((h, _) =>
        {
            if (DisplayNative.Eligible(h) && string.Equals(DisplayNative.Executable(h), executable, StringComparison.OrdinalIgnoreCase))
            {
                Place(h, rule);
                DisplayNative.GetWindowThreadProcessId(h, out var pid);
                _seen[h] = (pid, DateTime.UtcNow, true);
            }
            return true;
        }, IntPtr.Zero);
    }
    private void Place(IntPtr h, DisplayRule rule)
    {
        var oldDpi = DisplayNative.SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var screens = DisplayNative.Screens();
            var (screen, borderless) = ResolveTarget(screens, Settings.DefaultScreenId, rule);
            if (screen == null) return;
            DisplayNative.GetWindowThreadProcessId(h, out var pid);
            DisplayNative.GetWindowRect(h, out var rect);
            bool wasMaximized = DisplayNative.IsZoomed(h);
            long style = DisplayNative.GetStyle(h, -16).ToInt64();
            if (borderless)
            {
                if (!_original.ContainsKey(h)) _original[h] = (pid, style, rect);
                DisplayNative.ShowWindow(h, 9);
                DisplayNative.SetStyle(h, -16, new IntPtr(style & ~0x01CF0000L));
                var b = screen.Bounds;
                if (!DisplayNative.SetWindowPos(h, IntPtr.Zero, b.X, b.Y, b.Width, b.Height, 0x0034)) Log("Не удалось разместить окно: " + Marshal.GetLastWin32Error());
            }
            else
            {
                if (_original.Remove(h, out var original) && original.Pid == pid)
                { DisplayNative.SetStyle(h, -16, new IntPtr(original.Style)); rect = original.Rect; wasMaximized = (original.Style & 0x01000000L) != 0; }
                var b = screen.WorkingArea;
                int width = Math.Clamp(rect.Right - rect.Left, 160, Math.Max(160, b.Width));
                int height = Math.Clamp(rect.Bottom - rect.Top, 100, Math.Max(100, b.Height));
                if (wasMaximized) DisplayNative.ShowWindow(h, 9);
                DisplayNative.SetWindowPos(h, IntPtr.Zero, b.X + Math.Max(0, (b.Width-width)/2), b.Y + Math.Max(0, (b.Height-height)/2), width, height, 0x0034);
                if (wasMaximized) DisplayNative.ShowWindow(h, 3);
            }
        }
        finally { if (oldDpi != IntPtr.Zero) DisplayNative.SetThreadDpiAwarenessContext(oldDpi); }
    }
    public void Dispose() => _timer.Stop();
}
