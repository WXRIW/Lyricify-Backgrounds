using Lyricify.Backgrounds.Hosting.WinUI;

namespace Lyricify.Backgrounds.AppleMusicInspired.Ios.WinUI;

public sealed class AppleMusicIosBackground : AppleMusicIosBackgroundBase
{
    public AppleMusicIosBackground(
        AppleMusicIosBackgroundSettings? settings = null,
        int presetSlot = -1,
        Func<string?>? audioEndpointIdProvider = null)
        : base(
            settings ?? new AppleMusicIosBackgroundSettings(),
            presetSlot,
            audioEndpointIdProvider)
    {
    }
}

public sealed class AppleMusicIosWinUIBackgroundFactory :
    IWinUIBackgroundFactory
{
    private readonly int presetSlot;
    private readonly Func<string?>? audioEndpointIdProvider;

    public AppleMusicIosWinUIBackgroundFactory(
        int presetSlot = -1,
        Func<string?>? audioEndpointIdProvider = null)
    {
        this.presetSlot = presetSlot;
        this.audioEndpointIdProvider = audioEndpointIdProvider;
    }

    public IBackgroundProvider Provider { get; } =
        new AppleMusicIosBackgroundProvider();

    public WinUIBackgroundInstance Create(IBackgroundSettings settings)
    {
        if (settings is not AppleMusicIosBackgroundSettings typedSettings)
        {
            throw new ArgumentException(
                $"{Provider.Id} requires {nameof(AppleMusicIosBackgroundSettings)}.",
                nameof(settings));
        }

        var background = new AppleMusicIosBackground(
            typedSettings,
            presetSlot,
            audioEndpointIdProvider);
        return new WinUIBackgroundInstance(background, background);
    }
}
