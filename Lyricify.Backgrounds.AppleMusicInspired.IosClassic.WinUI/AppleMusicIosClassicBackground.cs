using Lyricify.Backgrounds.Hosting.WinUI;

namespace Lyricify.Backgrounds.AppleMusicInspired.IosClassic.WinUI;

public sealed class AppleMusicIosClassicBackground : AppleMusicIosClassicBackgroundBase
{
    public AppleMusicIosClassicBackground(
        AppleMusicIosClassicBackgroundSettings? settings = null,
        int presetSlot = -1,
        Func<string?>? audioEndpointIdProvider = null)
        : base(
            settings ?? new AppleMusicIosClassicBackgroundSettings(),
            presetSlot,
            audioEndpointIdProvider)
    {
    }
}

public sealed class AppleMusicIosClassicWinUIBackgroundFactory :
    IWinUIBackgroundFactory
{
    private readonly int presetSlot;
    private readonly Func<string?>? audioEndpointIdProvider;

    public AppleMusicIosClassicWinUIBackgroundFactory(
        int presetSlot = -1,
        Func<string?>? audioEndpointIdProvider = null)
    {
        this.presetSlot = presetSlot;
        this.audioEndpointIdProvider = audioEndpointIdProvider;
    }

    public IBackgroundProvider Provider { get; } =
        new AppleMusicIosClassicBackgroundProvider();

    public WinUIBackgroundInstance Create(IBackgroundSettings settings)
    {
        if (settings is not AppleMusicIosClassicBackgroundSettings typedSettings)
        {
            throw new ArgumentException(
                $"{Provider.Id} requires {nameof(AppleMusicIosClassicBackgroundSettings)}.",
                nameof(settings));
        }

        var background = new AppleMusicIosClassicBackground(
            typedSettings,
            presetSlot,
            audioEndpointIdProvider);
        return new WinUIBackgroundInstance(background, background);
    }
}
