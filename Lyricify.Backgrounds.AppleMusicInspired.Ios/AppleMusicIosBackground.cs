namespace Lyricify.Backgrounds.AppleMusicInspired.Ios;

public sealed class AppleMusicIosBackgroundSettings : IBackgroundSettings
{
    public int? FrameRateLimit { get; set; } = -1;
    public double RenderScale { get; set; } = 1d;
    public double RotationScale { get; set; } = 1d;
    public double BassPulseScale { get; set; } = 1d;
    public double BlurScale { get; set; } = 1d;
    public int PortraitControlPointCount { get; set; } = -1;
    public int PortraitSubdivisionLevels { get; set; } = -1;
    public int LandscapeControlPointCount { get; set; } = -1;
    public int LandscapeSubdivisionLevels { get; set; } = -1;

    public AppleMusicIosBackgroundSettings Clone() =>
        (AppleMusicIosBackgroundSettings)MemberwiseClone();
}

public sealed class AppleMusicIosBackgroundProvider : IBackgroundProvider
{
    public const string BackgroundId =
        "lyricify.backgrounds.apple-music-inspired.ios";

    public string Id => BackgroundId;

    public string DisplayName => "Apple Music Inspired · iOS";

    public IBackgroundSettings CreateDefaultSettings() =>
        new AppleMusicIosBackgroundSettings();
}
