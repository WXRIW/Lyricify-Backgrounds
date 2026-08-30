using Lyricify.Backgrounds.Hosting.Wpf;
using System.Drawing;
using System.Windows.Controls;

namespace Lyricify.Backgrounds.AppleMusicInspired.IosClassic.Wpf;

public sealed class AppleMusicIosClassicBackground : Grid, IBackgroundSession
{
    private readonly AppleMusicIosClassicRendererView renderer;
    private bool isPlaying = true;
    private bool isReady;
    private bool disposed;

    public AppleMusicIosClassicBackground(
        AppleMusicIosClassicBackgroundSettings? settings = null,
        bool lightTheme = false,
        Func<int>? deviceLatencyProvider = null,
        Func<string, Task<Bitmap>>? artworkLoader = null,
        Func<string?>? audioEndpointIdProvider = null,
        int presetSlot = -1)
    {
        renderer = new AppleMusicIosClassicRendererView(
            settings ?? new AppleMusicIosClassicBackgroundSettings(),
            lightTheme,
            () => isPlaying,
            firstCompositionFramePresented: RaiseFirstFramePresented,
            deviceLatencyProvider: deviceLatencyProvider,
            artworkLoader: artworkLoader,
            presetSlot: presetSlot,
            audioEndpointIdProvider: audioEndpointIdProvider);
        Background = System.Windows.Media.Brushes.Black;
        IsHitTestVisible = false;
        Children.Add(renderer);
    }

    public int PresetIndex => renderer.PresetIndex;

    public int LandscapePresetIndex => renderer.LandscapePresetIndex;

    public bool IsReady => isReady;

    public event EventHandler? FirstFramePresented;

    public event EventHandler<BackgroundFaultedEventArgs>? Faulted;

    public void UpdateState(BackgroundState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (disposed) throw new ObjectDisposedException(nameof(AppleMusicIosClassicBackground));
        isPlaying = state.IsPlaying;
        renderer.SetVerticalLayout(state.IsVertical);
        renderer.SetLightTheme(state.IsLightTheme);
        renderer.SetIsBehindLyrics(state.IsBehindLyrics);
        renderer.SetPresentationVisible(state.IsVisible);
    }

    public async Task SetArtworkAsync(
        BackgroundArtwork artwork,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artwork);
        if (disposed) throw new ObjectDisposedException(nameof(AppleMusicIosClassicBackground));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await renderer.SetArtworkAsync(artwork);
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(this, new BackgroundFaultedEventArgs(exception));
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        isReady = false;
        renderer.SetPresentationVisible(false);
        Children.Clear();
    }

    private void RaiseFirstFramePresented()
    {
        if (disposed || isReady) return;
        isReady = true;
        FirstFramePresented?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class AppleMusicIosClassicWpfBackgroundFactory :
    IWpfBackgroundFactory
{
    private readonly bool lightTheme;
    private readonly Func<int>? deviceLatencyProvider;
    private readonly Func<string, Task<Bitmap>>? artworkLoader;
    private readonly Func<string?>? audioEndpointIdProvider;
    private readonly int presetSlot;

    public AppleMusicIosClassicWpfBackgroundFactory(
        bool lightTheme = false,
        Func<int>? deviceLatencyProvider = null,
        Func<string, Task<Bitmap>>? artworkLoader = null,
        Func<string?>? audioEndpointIdProvider = null,
        int presetSlot = -1)
    {
        this.lightTheme = lightTheme;
        this.deviceLatencyProvider = deviceLatencyProvider;
        this.artworkLoader = artworkLoader;
        this.audioEndpointIdProvider = audioEndpointIdProvider;
        this.presetSlot = presetSlot;
    }

    public IBackgroundProvider Provider { get; } =
        new AppleMusicIosClassicBackgroundProvider();

    public WpfBackgroundInstance Create(IBackgroundSettings settings)
    {
        if (settings is not AppleMusicIosClassicBackgroundSettings typedSettings)
        {
            throw new ArgumentException(
                $"{Provider.Id} requires {nameof(AppleMusicIosClassicBackgroundSettings)}.",
                nameof(settings));
        }

        var background = new AppleMusicIosClassicBackground(
            typedSettings,
            lightTheme,
            deviceLatencyProvider,
            artworkLoader,
            audioEndpointIdProvider,
            presetSlot);
        return new WpfBackgroundInstance(background, background);
    }
}
