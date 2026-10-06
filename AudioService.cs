using System;
using System.Collections.Generic;
using System.Linq;
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

        try
        {
            defaultId = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
        }
        catch
        {
        }

        var result = new List<AudioDeviceItem>();

        foreach (var device in devices)
        {
            var volume = 0;
            var muted = false;

            try
            {
                volume = (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                muted = device.AudioEndpointVolume.Mute;
            }
            catch
            {
            }

            result.Add(new AudioDeviceItem
            {
                Id = device.ID,
                Name = CleanName(device.FriendlyName),
                Subtitle = device.ID == defaultId ? "Сейчас используется" : "Нажми, чтобы переключить",
                IsDefault = device.ID == defaultId,
                Glyph = GuessGlyph(device.FriendlyName),
                VolumePercent = Math.Clamp(volume, 0, 100),
                IsMuted = muted
            });
        }

        return result
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public string? GetDefaultDeviceId()
    {
        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
        }
        catch
        {
            return null;
        }
    }

    public void SetDefault(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();

        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
    }

    public void SetVolume(string deviceId, int percent)
    {
        using var device = _enumerator.GetDevice(deviceId);
        device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent, 0, 100) / 100f;
    }

    public bool ToggleMute(string deviceId)
    {
        using var device = _enumerator.GetDevice(deviceId);
        var newValue = !device.AudioEndpointVolume.Mute;
        device.AudioEndpointVolume.Mute = newValue;
        return newValue;
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

        if (n.Contains("tv") || n.Contains("телев") || n.Contains("hdmi") || n.Contains("display"))
            return "📺";

        if (n.Contains("head") || n.Contains("науш") || n.Contains("airpods") || n.Contains("buds"))
            return "🎧";

        if (n.Contains("speaker") || n.Contains("колон") || n.Contains("realtek"))
            return "🔊";

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
    private class PolicyConfigClient
    {
    }
}
