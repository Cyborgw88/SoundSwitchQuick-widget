namespace SoundSwitchQuick;

public sealed class AudioDeviceItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Subtitle { get; init; } = "Готово к использованию";
    public bool IsDefault { get; init; }
    public string Glyph { get; init; } = "🔊";
}
