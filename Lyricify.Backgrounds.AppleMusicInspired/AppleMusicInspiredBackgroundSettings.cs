namespace Lyricify.Backgrounds.AppleMusicInspired
{
    public sealed class AppleMusicInspiredBackgroundSettings : IBackgroundSettings
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

        public AppleMusicInspiredBackgroundSettings Clone() =>
            (AppleMusicInspiredBackgroundSettings)MemberwiseClone();
    }

    public sealed class AppleMusicInspiredBackgroundProvider : IBackgroundProvider
    {
        public const string BackgroundId = "lyricify.apple-music-inspired";

        public string Id => BackgroundId;
        public string DisplayName => "Apple Music Inspired";
        public IBackgroundSettings CreateDefaultSettings() =>
            new AppleMusicInspiredBackgroundSettings();
    }
}
