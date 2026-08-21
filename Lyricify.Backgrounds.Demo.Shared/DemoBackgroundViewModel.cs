using Lyricify.Backgrounds.AppleMusicInspired;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lyricify.Backgrounds.Demo.Shared;

public sealed class DemoBackgroundViewModel : INotifyPropertyChanged
{
    private bool isPlaying = true;
    private bool isVertical = true;
    private bool isLightTheme;
    private bool isBehindLyrics = true;
    private string artworkUrl = string.Empty;
    private string status = "Select an artwork or enter an image URL.";
    private AppleMusicInspiredBackgroundSettings settings = new();
    private int selectedPresetIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppleMusicInspiredBackgroundSettings Settings => settings;
    public IReadOnlyList<string> PresetOptions { get; } =
        ["Random", "Preset 1", "Preset 2", "Preset 3", "Preset 4", "Preset 5"];
    public int SelectedPresetIndex
    {
        get => selectedPresetIndex;
        set => Set(ref selectedPresetIndex, Math.Clamp(value, 0, PresetOptions.Count - 1));
    }
    public bool IsPlaying { get => isPlaying; set => Set(ref isPlaying, value); }
    public bool IsVertical { get => isVertical; set => Set(ref isVertical, value); }
    public bool IsLightTheme { get => isLightTheme; set => Set(ref isLightTheme, value); }
    public bool IsBehindLyrics { get => isBehindLyrics; set => Set(ref isBehindLyrics, value); }
    public string ArtworkUrl { get => artworkUrl; set => Set(ref artworkUrl, value); }
    public string Status { get => status; set => Set(ref status, value); }

    public int FrameRateLimit
    {
        get => settings.FrameRateLimit ?? -1;
        set { if (settings.FrameRateLimit == value) return; settings.FrameRateLimit = value; Changed(); }
    }

    public double RenderScale
    {
        get => settings.RenderScale;
        set { if (settings.RenderScale == value) return; settings.RenderScale = value; Changed(); }
    }

    public double RotationScale
    {
        get => settings.RotationScale;
        set { if (settings.RotationScale == value) return; settings.RotationScale = value; Changed(); }
    }

    public double BassPulseScale
    {
        get => settings.BassPulseScale;
        set { if (settings.BassPulseScale == value) return; settings.BassPulseScale = value; Changed(); }
    }

    public double BlurScale
    {
        get => settings.BlurScale;
        set { if (settings.BlurScale == value) return; settings.BlurScale = value; Changed(); }
    }

    public int PortraitControlPointCount
    {
        get => settings.PortraitControlPointCount;
        set { if (settings.PortraitControlPointCount == value) return; settings.PortraitControlPointCount = value; Changed(); }
    }

    public int PortraitSubdivisionLevels
    {
        get => settings.PortraitSubdivisionLevels;
        set { if (settings.PortraitSubdivisionLevels == value) return; settings.PortraitSubdivisionLevels = value; Changed(); }
    }

    public int LandscapeControlPointCount
    {
        get => settings.LandscapeControlPointCount;
        set { if (settings.LandscapeControlPointCount == value) return; settings.LandscapeControlPointCount = value; Changed(); }
    }

    public int LandscapeSubdivisionLevels
    {
        get => settings.LandscapeSubdivisionLevels;
        set { if (settings.LandscapeSubdivisionLevels == value) return; settings.LandscapeSubdivisionLevels = value; Changed(); }
    }

    public void Reset()
    {
        settings = new AppleMusicInspiredBackgroundSettings();
        SelectedPresetIndex = 0;
        IsPlaying = true;
        IsVertical = true;
        IsLightTheme = false;
        IsBehindLyrics = true;
        Changed(string.Empty);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed(propertyName);
    }

    private void Changed([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
