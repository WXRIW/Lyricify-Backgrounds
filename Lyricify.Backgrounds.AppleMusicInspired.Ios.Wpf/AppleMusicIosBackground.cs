using Lyricify.Backgrounds.Hosting.Wpf;
using System.Drawing;
using System.Windows.Controls;

namespace Lyricify.Backgrounds.AppleMusicInspired.Ios.Wpf;

public sealed class AppleMusicIosBackground : Grid, IBackgroundSession
{
    private readonly AppleMusicIosRendererView renderer;
    private bool isPlaying = true;
    private bool isReady;
    private bool disposed;

    public AppleMusicIosBackground(
        AppleMusicIosBackgroundSettings? settings = null,
        bool lightTheme = false,
        Func<int>? deviceLatencyProvider = null,
        Func<string, Task<Bitmap>>? artworkLoader = null,
        Func<string?>? audioEndpointIdProvider = null,
        int presetSlot = -1)
    {
        renderer = new AppleMusicIosRendererView(
            settings ?? new AppleMusicIosBackgroundSettings(),
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

    public void SetArtwork(string url, string id)
    {
        ThrowIfDisposed();
        renderer.SetArtwork(url, id);
    }

    public async Task SetArtworkAsync(Bitmap artwork, string id)
    {
        ArgumentNullException.ThrowIfNull(artwork);
        ThrowIfDisposed();
        try
        {
            await renderer.SetArtworkAsync(artwork, id);
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(this, new BackgroundFaultedEventArgs(exception));
            throw;
        }
    }

    public void SetPlaying(bool playing)
    {
        ThrowIfDisposed();
        isPlaying = playing;
    }

    public void SetVerticalLayout(bool vertical, bool animate = true)
    {
        ThrowIfDisposed();
        renderer.SetVerticalLayout(vertical, animate);
    }

    public void SetLightTheme(bool lightTheme)
    {
        ThrowIfDisposed();
        renderer.SetLightTheme(lightTheme);
    }

    public void SetIsBehindLyrics(bool behindLyrics)
    {
        ThrowIfDisposed();
        renderer.SetIsBehindLyrics(behindLyrics);
    }

    public void SetPresentationVisible(bool visible)
    {
        ThrowIfDisposed();
        renderer.SetPresentationVisible(visible);
    }

    public void RefreshAudioEndpoint()
    {
        ThrowIfDisposed();
        renderer.RefreshAudioEndpoint();
    }

    public void UpdateState(BackgroundState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SetPlaying(state.IsPlaying);
        SetVerticalLayout(state.IsVertical);
        SetLightTheme(state.IsLightTheme);
        SetIsBehindLyrics(state.IsBehindLyrics);
        SetPresentationVisible(state.IsVisible);
    }

    public async Task SetArtworkAsync(
        BackgroundArtwork artwork,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artwork);
        ThrowIfDisposed();
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

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(AppleMusicIosBackground));
        }
    }
}

public sealed class AppleMusicIosWpfBackgroundFactory : IWpfBackgroundFactory
{
    private readonly bool lightTheme;
    private readonly Func<int>? deviceLatencyProvider;
    private readonly Func<string, Task<Bitmap>>? artworkLoader;
    private readonly Func<string?>? audioEndpointIdProvider;
    private readonly int presetSlot;

    public AppleMusicIosWpfBackgroundFactory(
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
        new AppleMusicIosBackgroundProvider();

    public WpfBackgroundInstance Create(IBackgroundSettings settings)
    {
        if (settings is not AppleMusicIosBackgroundSettings typedSettings)
        {
            throw new ArgumentException(
                $"{Provider.Id} requires {nameof(AppleMusicIosBackgroundSettings)}.",
                nameof(settings));
        }

        var background = new AppleMusicIosBackground(
            typedSettings,
            lightTheme,
            deviceLatencyProvider,
            artworkLoader,
            audioEndpointIdProvider,
            presetSlot);
        return new WpfBackgroundInstance(background, background);
    }
}
