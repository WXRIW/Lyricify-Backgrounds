namespace Lyricify.Backgrounds.AppleMusicInspired.IosClassic;

public sealed class AppleMusicIosClassicBackgroundSettings : IBackgroundSettings
{
    public int? FrameRateLimit { get; set; } = -1;
    public double RenderScale { get; set; } = 1d;
    public double RotationScale { get; set; } = 1d;
    public double BassPulseScale { get; set; } = 0d;
    public double BlurScale { get; set; } = 1d;
    public int PortraitControlPointCount { get; set; } = -1;
    public int PortraitSubdivisionLevels { get; set; } = 3;
    public int LandscapeControlPointCount { get; set; } = -1;
    public int LandscapeSubdivisionLevels { get; set; } = 3;

    public AppleMusicIosClassicBackgroundSettings Clone() =>
        (AppleMusicIosClassicBackgroundSettings)MemberwiseClone();
}

public sealed class AppleMusicIosClassicBackgroundProvider : IBackgroundProvider
{
    public const string BackgroundId =
        "lyricify.backgrounds.apple-music-inspired.ios-classic";

    public string Id => BackgroundId;

    public string DisplayName => "Apple Music Inspired · iOS Classic";

    public IBackgroundSettings CreateDefaultSettings() =>
        new AppleMusicIosClassicBackgroundSettings();
}
