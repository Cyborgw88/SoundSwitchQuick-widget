using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace SoundSwitchQuick;

public sealed class AudioService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public IReadOnlyList<AudioDeviceItem> GetPlaybackDevices()
    {
        var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        string? defaultId = null;
        try { defaultId = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
        catch { }

        return devices
            .Select(d => new AudioDeviceItem
            {
                Id = d.ID,
                Name = CleanName(d.FriendlyName),
                Subtitle = d.ID == defaultId ? "Сейчас используется" : "Нажми, чтобы переключить",
                IsDefault = d.ID == defaultId,
                Glyph = GuessGlyph(d.FriendlyName)
            })
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void SetDefault(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
        {
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
        }
    }

    private static string CleanName(string name)
    {
        return name.Replace(" (High Definition Audio Device)", "", StringComparison.OrdinalIgnoreCase)
                   .Replace(" (NVIDIA High Definition Audio)", "", StringComparison.OrdinalIgnoreCase)
                   .Trim();
    }

    private static string GuessGlyph(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("tv") || n.Contains("телев") || n.Contains("hdmi") || n.Contains("display")) return "📺";
        if (n.Contains("head") || n.Contains("науш") || n.Contains("airpods") || n.Contains("buds")) return "🎧";
        if (n.Contains("speaker") || n.Contains("колон") || n.Contains("realtek")) return "🔊";
        return "🔉";
    }

    public void Dispose() => _enumerator.Dispose();

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat();
        int GetDeviceFormat();
        int ResetDeviceFormat();
        int SetDeviceFormat();
        int GetProcessingPeriod();
        int SetProcessingPeriod();
        int GetShareMode();
        int SetShareMode();
        int GetPropertyValue();
        int SetPropertyValue();
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ERole role);
        int SetEndpointVisibility();
    }

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }
}
