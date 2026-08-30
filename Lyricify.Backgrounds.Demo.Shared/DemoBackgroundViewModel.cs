using Lyricify.Backgrounds;
using Lyricify.Backgrounds.AppleMusicInspired.Ios;
using Lyricify.Backgrounds.AppleMusicInspired.IosClassic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lyricify.Backgrounds.Demo.Shared;

public sealed class DemoBackgroundViewModel : INotifyPropertyChanged
{
    private static readonly IBackgroundProvider[] Providers =
    [
        new AppleMusicIosBackgroundProvider(),
        new AppleMusicIosClassicBackgroundProvider(),
    ];

    private bool isPlaying = true;
    private bool isVertical = true;
    private bool isLightTheme = true;
    private bool isBehindLyrics = true;
    private string artworkUrl = string.Empty;
    private string status = "Select an artwork or enter an image URL.";
    private AppleMusicIosBackgroundSettings iosSettings = new();
    private AppleMusicIosClassicBackgroundSettings classicSettings = new();
    private int selectedBackgroundIndex;
    private int selectedPresetIndex;

    public DemoBackgroundViewModel()
    {
        const string argumentPrefix = "--background=";
        string? requestedId = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith(
                argumentPrefix,
                StringComparison.OrdinalIgnoreCase))?[argumentPrefix.Length..]
            ?? Environment.GetEnvironmentVariable("LYRICIFY_BACKGROUNDS_DEMO_ID");
        int requestedIndex = Array.FindIndex(
            Providers,
            provider => string.Equals(provider.Id, requestedId, StringComparison.Ordinal));
        if (requestedIndex >= 0)
        {
            selectedBackgroundIndex = requestedIndex;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IBackgroundSettings Settings => selectedBackgroundIndex == 0
        ? iosSettings
        : classicSettings;

    public IReadOnlyList<string> BackgroundOptions { get; } =
        Providers.Select(provider => provider.DisplayName).ToArray();

    public string SelectedBackgroundId => Providers[selectedBackgroundIndex].Id;

    public int SelectedBackgroundIndex
    {
        get => selectedBackgroundIndex;
        set
        {
            int next = Math.Clamp(value, 0, Providers.Length - 1);
            if (selectedBackgroundIndex == next) return;
            selectedBackgroundIndex = next;
            Changed(nameof(SelectedBackgroundIndex));
            Changed(nameof(SelectedBackgroundId));
            Changed(nameof(Settings));
            NotifySettingsChanged();
        }
    }

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
        get => GetSetting(settings => settings.FrameRateLimit ?? -1, settings => settings.FrameRateLimit ?? -1);
        set => SetSetting(value, settings => settings.FrameRateLimit ?? -1, (settings, next) => settings.FrameRateLimit = next, settings => settings.FrameRateLimit ?? -1, (settings, next) => settings.FrameRateLimit = next);
    }

    public double RenderScale
    {
        get => GetSetting(settings => settings.RenderScale, settings => settings.RenderScale);
        set => SetSetting(value, settings => settings.RenderScale, (settings, next) => settings.RenderScale = next, settings => settings.RenderScale, (settings, next) => settings.RenderScale = next);
    }

    public double RotationScale
    {
        get => GetSetting(settings => settings.RotationScale, settings => settings.RotationScale);
        set => SetSetting(value, settings => settings.RotationScale, (settings, next) => settings.RotationScale = next, settings => settings.RotationScale, (settings, next) => settings.RotationScale = next);
    }

    public double BassPulseScale
    {
        get => GetSetting(settings => settings.BassPulseScale, settings => settings.BassPulseScale);
        set => SetSetting(value, settings => settings.BassPulseScale, (settings, next) => settings.BassPulseScale = next, settings => settings.BassPulseScale, (settings, next) => settings.BassPulseScale = next);
    }

    public double BlurScale
    {
        get => GetSetting(settings => settings.BlurScale, settings => settings.BlurScale);
        set => SetSetting(value, settings => settings.BlurScale, (settings, next) => settings.BlurScale = next, settings => settings.BlurScale, (settings, next) => settings.BlurScale = next);
    }

    public int PortraitControlPointCount
    {
        get => GetSetting(settings => settings.PortraitControlPointCount, settings => settings.PortraitControlPointCount);
        set => SetSetting(value, settings => settings.PortraitControlPointCount, (settings, next) => settings.PortraitControlPointCount = next, settings => settings.PortraitControlPointCount, (settings, next) => settings.PortraitControlPointCount = next);
    }

    public int PortraitSubdivisionLevels
    {
        get => GetSetting(settings => settings.PortraitSubdivisionLevels, settings => settings.PortraitSubdivisionLevels);
        set => SetSetting(value, settings => settings.PortraitSubdivisionLevels, (settings, next) => settings.PortraitSubdivisionLevels = next, settings => settings.PortraitSubdivisionLevels, (settings, next) => settings.PortraitSubdivisionLevels = next);
    }

    public int LandscapeControlPointCount
    {
        get => GetSetting(settings => settings.LandscapeControlPointCount, settings => settings.LandscapeControlPointCount);
        set => SetSetting(value, settings => settings.LandscapeControlPointCount, (settings, next) => settings.LandscapeControlPointCount = next, settings => settings.LandscapeControlPointCount, (settings, next) => settings.LandscapeControlPointCount = next);
    }

    public int LandscapeSubdivisionLevels
    {
        get => GetSetting(settings => settings.LandscapeSubdivisionLevels, settings => settings.LandscapeSubdivisionLevels);
        set => SetSetting(value, settings => settings.LandscapeSubdivisionLevels, (settings, next) => settings.LandscapeSubdivisionLevels = next, settings => settings.LandscapeSubdivisionLevels, (settings, next) => settings.LandscapeSubdivisionLevels = next);
    }

    public void Reset()
    {
        iosSettings = new AppleMusicIosBackgroundSettings();
        classicSettings = new AppleMusicIosClassicBackgroundSettings();
        selectedPresetIndex = 0;
        isPlaying = true;
        isVertical = true;
        isLightTheme = true;
        isBehindLyrics = true;
        Changed(string.Empty);
    }

    private void NotifySettingsChanged()
    {
        Changed(nameof(FrameRateLimit));
        Changed(nameof(RenderScale));
        Changed(nameof(RotationScale));
        Changed(nameof(BassPulseScale));
        Changed(nameof(BlurScale));
        Changed(nameof(PortraitControlPointCount));
        Changed(nameof(PortraitSubdivisionLevels));
        Changed(nameof(LandscapeControlPointCount));
        Changed(nameof(LandscapeSubdivisionLevels));
    }

    private T GetSetting<T>(
        Func<AppleMusicIosBackgroundSettings, T> iosGetter,
        Func<AppleMusicIosClassicBackgroundSettings, T> classicGetter) =>
        selectedBackgroundIndex == 0
            ? iosGetter(iosSettings)
            : classicGetter(classicSettings);

    private void SetSetting<T>(
        T value,
        Func<AppleMusicIosBackgroundSettings, T> iosGetter,
        Action<AppleMusicIosBackgroundSettings, T> iosSetter,
        Func<AppleMusicIosClassicBackgroundSettings, T> classicGetter,
        Action<AppleMusicIosClassicBackgroundSettings, T> classicSetter,
        [CallerMemberName] string? propertyName = null)
    {
        if (selectedBackgroundIndex == 0)
        {
            if (EqualityComparer<T>.Default.Equals(iosGetter(iosSettings), value)) return;
            iosSetter(iosSettings, value);
        }
        else
        {
            if (EqualityComparer<T>.Default.Equals(classicGetter(classicSettings), value)) return;
            classicSetter(classicSettings, value);
        }

        Changed(propertyName);
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
