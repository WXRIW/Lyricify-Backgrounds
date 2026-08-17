using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lyricify.Backgrounds.AppleMusicInspired.WinUI;

/// <summary>
/// Native WinUI adapter. The shared HLSL pipeline renders into a composition
/// swap chain owned by a SwapChainPanel, so XAML content can be layered above it.
/// </summary>
public sealed class AppleMusicInspiredBackground : Grid, IBackgroundSession
{
    private readonly SwapChainPanel panel = new();
    private readonly AppleMusicInspiredBackgroundSettings settings;
    private readonly Func<string?>? audioEndpointIdProvider;
    private BackgroundState state = new() { IsVisible = true, IsPlaying = true };
    private SwapChainPanelPresenter? presenter;
    private AppleMusicInspiredRenderer? renderer;
    private byte[]? artwork;
    private int presetSlot;
    private bool disposed;
    private bool renderingHooked;

    public AppleMusicInspiredBackground(
        AppleMusicInspiredBackgroundSettings? settings = null,
        int presetSlot = -1,
        Func<string?>? audioEndpointIdProvider = null)
    {
        this.settings = settings?.Clone() ?? new AppleMusicInspiredBackgroundSettings();
        this.presetSlot = presetSlot;
        this.audioEndpointIdProvider = audioEndpointIdProvider;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
        IsHitTestVisible = false;
        Children.Add(panel);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        panel.CompositionScaleChanged += OnCompositionScaleChanged;
    }

    public int PresetIndex => renderer?.PresetIndex ?? -1;
    public int LandscapePresetIndex => renderer?.LandscapePresetIndex ?? -1;
    public bool IsReady { get; private set; }
    public event EventHandler? FirstFramePresented;
    public event EventHandler<BackgroundFaultedEventArgs>? Faulted;

    public void ApplySettings(AppleMusicInspiredBackgroundSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        bool recreateMesh =
            settings.PortraitControlPointCount != value.PortraitControlPointCount ||
            settings.PortraitSubdivisionLevels != value.PortraitSubdivisionLevels ||
            settings.LandscapeControlPointCount != value.LandscapeControlPointCount ||
            settings.LandscapeSubdivisionLevels != value.LandscapeSubdivisionLevels;

        settings.FrameRateLimit = value.FrameRateLimit;
        settings.RenderScale = value.RenderScale;
        settings.BassPulseScale = value.BassPulseScale;
        settings.BlurScale = value.BlurScale;
        settings.PortraitControlPointCount = value.PortraitControlPointCount;
        settings.PortraitSubdivisionLevels = value.PortraitSubdivisionLevels;
        settings.LandscapeControlPointCount = value.LandscapeControlPointCount;
        settings.LandscapeSubdivisionLevels = value.LandscapeSubdivisionLevels;

        renderer?.ApplySettings(recreateMesh);
    }

    public void SetPreset(int value)
    {
        if (presetSlot == value) return;
        presetSlot = value;
        if (IsLoaded) RecreateRenderer();
    }

    public void RefreshAudioEndpoint()
    {
        renderer?.RefreshAudioEndpoint();
    }

    public void UpdateState(BackgroundState value)
    {
        BackgroundState next = value?.Clone() ?? throw new ArgumentNullException(nameof(value));
        bool themeChanged = state.IsLightTheme != next.IsLightTheme;
        state = next;
        Visibility = state.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (themeChanged && IsLoaded)
        {
            RecreateRenderer();
            return;
        }
        renderer?.SetVerticalLayout(state.IsVertical);
        renderer?.SetIsBehindLyrics(state.IsBehindLyrics);
        renderer?.SetPresentationVisible(state.IsVisible);
    }

    public async Task SetArtworkAsync(byte[] encodedData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encodedData);
        artwork = encodedData.ToArray();
        await ApplyArtworkAsync(cancellationToken);
    }

    public Task SetArtworkAsync(BackgroundArtwork value, CancellationToken cancellationToken = default) =>
        SetArtworkAsync(value?.EncodedData ?? throw new ArgumentNullException(nameof(value)), cancellationToken);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CreateRenderer();
            HookRendering();
        }
        catch (Exception exception)
        {
            ReleaseRenderer();
            Faulted?.Invoke(this, new BackgroundFaultedEventArgs(exception));
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnhookRendering();
        ReleaseRenderer();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateRendererSize();

    private void OnCompositionScaleChanged(SwapChainPanel sender, object args) =>
        UpdateRendererSize();

    private void CreateRenderer()
    {
        if (renderer != null) return;
        presenter = new SwapChainPanelPresenter(panel, RaiseFirstFrame);
        renderer = new AppleMusicInspiredRenderer(
            settings,
            state.IsLightTheme,
            () => state.IsPlaying,
            presenter,
            RaiseFirstFrame,
            presetSlot: presetSlot,
            audioEndpointIdProvider: audioEndpointIdProvider);
        renderer.SetVerticalLayout(state.IsVertical, false);
        renderer.SetIsBehindLyrics(state.IsBehindLyrics);
        renderer.SetPresentationVisible(state.IsVisible);
        UpdateRendererSize();
        renderer.Initialize();
        _ = ApplyArtworkAsync(CancellationToken.None);
    }

    private void RecreateRenderer()
    {
        ReleaseRenderer();
        IsReady = false;
        CreateRenderer();
    }

    private async Task ApplyArtworkAsync(CancellationToken cancellationToken)
    {
        if (artwork == null || renderer == null) return;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using System.Drawing.Bitmap bitmap = AppleMusicInspiredArtworkLoader.Decode(artwork);
            await renderer.SetArtworkAsync(bitmap, Guid.NewGuid().ToString("N"));
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(this, new BackgroundFaultedEventArgs(exception));
            throw;
        }
    }

    private void UpdateRendererSize()
    {
        renderer?.Resize(
            ActualWidth,
            ActualHeight,
            panel.CompositionScaleX,
            panel.CompositionScaleY);
    }

    private void HookRendering()
    {
        if (renderingHooked) return;
        CompositionTarget.Rendering += OnRendering;
        renderingHooked = true;
    }

    private void UnhookRendering()
    {
        if (!renderingHooked) return;
        CompositionTarget.Rendering -= OnRendering;
        renderingHooked = false;
    }

    private void OnRendering(object? sender, object e)
    {
        try
        {
            renderer?.Render();
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(this, new BackgroundFaultedEventArgs(exception));
        }
    }

    private void RaiseFirstFrame()
    {
        if (IsReady) return;
        IsReady = true;
        FirstFramePresented?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseRenderer()
    {
        renderer?.Dispose();
        renderer = null;
        presenter?.Dispose();
        presenter = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        SizeChanged -= OnSizeChanged;
        panel.CompositionScaleChanged -= OnCompositionScaleChanged;
        UnhookRendering();
        ReleaseRenderer();
    }
}
