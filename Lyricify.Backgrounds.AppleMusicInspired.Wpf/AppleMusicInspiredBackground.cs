using Lyricify.Backgrounds.AppleMusicInspired.Rendering;
using Lyricify.Backgrounds.Hosting.Wpf;
using SharpGen.Runtime;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using Vortice.DXGI;
using Vortice.Mathematics;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using Format = Vortice.DXGI.Format;

#nullable disable
namespace Lyricify.Backgrounds.AppleMusicInspired.Wpf
{
    /// <summary>
    /// Direct3D renderer for the animated background material.
    /// </summary>
    public sealed class AppleMusicInspiredBackground : Grid
    {
        private const double ArtworkTransitionSeconds = 0.5;
        private const double LyricsModeTransitionSeconds = 0.25;
        private const int BlurSurfaceDownsample = 4;
        private const float GaussianKernelSigma = 42.5f;
        private const float LyricsBlurSigma = 42.5f;
        private const float OrdinaryBlurSigma = 80f;
        private const float DarkBehindLyricsBlackScrimAlpha = 0.4f;
        private const float LightAppearanceBlackScrimAlpha = 1f / 3f;
        private const float PortraitTextureScale = 1f;
        private const float LandscapeTextureScale = 0.8f;
        private const string ShaderResourceName =
            "Lyricify.Backgrounds.AppleMusicInspired.Resources.AppleMusicInspiredBackground.hlsl";

        private readonly System.Windows.Controls.Image _image;
        private readonly Stopwatch _animationClock = new();
        private readonly AppleMusicInspiredBackgroundSettings _settings;
        private readonly Func<bool> _isPlayingProvider;
        private readonly double _renderScale;
        private readonly long _minimumRenderIntervalTicks;
        private readonly bool _refreshDisabled;
        private readonly bool _lightTheme;
        private readonly IntPtr _compositionWindowHandle;
        private readonly Action _firstCompositionFramePresented;
        private readonly Func<string, Task<Bitmap>> _artworkLoader;
        private AppleMusicPinchVertex[] _meshVertices;
        private ushort[] _meshIndices;
        private readonly AppleMusicSpectrumAnalysis _spectrumAnalysis;

        private bool _isVerticalLayout = true;

        private D3DImage _d3DImage;
        private double _d3DImageDpiX = 96d;
        private double _d3DImageDpiY = 96d;
        private bool _presentationVisible = true;
        private bool _isBehindLyrics;
        private bool _lyricsModeTransitioning;
        private float _lyricsModeMix;
        private float _lyricsModeMixFrom;
        private float _lyricsModeMixTo;
        private double _lyricsModeTransitionStartTime;
        private bool _renderingHooked;
        private bool _reloading;
        private bool _forceNextRender = true;
        private bool _deviceRecoveryPending;
        private bool _deviceRecoveryScheduled;
        private long _nextRenderTimestamp;
        private int _artworkGeneration;
        private string _currentArtworkUrl = string.Empty;
        private string _currentTrackId = string.Empty;
        private double _transitionStartTime;
        private bool _transitioning;

        private ArtworkData _currentArtworkData;
        private ArtworkData _previousArtworkData;
        private GpuArtwork _currentArtwork;
        private GpuArtwork _previousArtwork;

        private ID3D11Device _device;
        private ID3D11DeviceContext _context;
        private CompositionSwapChainPresenter _compositionPresenter;
        private IDirect3D9Ex _direct3D9;
        private IDirect3DDevice9Ex _device9;
        private IDirect3DTexture9 _sharedTexture9;

        private RenderSurface _rotationSurface;
        private RenderSurface _horizontalBlurSurface;
        private RenderSurface _verticalBlurSurface;
        private RenderSurface _ordinaryBlurSurface;
        private RenderSurface _outputSurface;

        private ID3D11VertexShader _rotationVertexShader;
        private ID3D11VertexShader _artworkFillVertexShader;
        private ID3D11VertexShader _fullscreenVertexShader;
        private ID3D11VertexShader _pinchVertexShader;
        private ID3D11PixelShader _rotationPixelShader;
        private ID3D11PixelShader _horizontalBlurPixelShader;
        private ID3D11PixelShader _verticalBlurPixelShader;
        private ID3D11PixelShader _ordinaryMaterialPixelShader;
        private ID3D11PixelShader _materialTreatedPixelShader;
        private ID3D11PixelShader _materialCompositePixelShader;
        private ID3D11PixelShader _pinchPixelShader;
        private ID3D11PixelShader _pinchCompositePixelShader;
        private ID3D11InputLayout _quadInputLayout;
        private ID3D11InputLayout _pinchInputLayout;
        private ID3D11Buffer _quadVertexBuffer;
        private ID3D11Buffer _quadIndexBuffer;
        private ID3D11Buffer _pinchVertexBuffer;
        private ID3D11Buffer _pinchIndexBuffer;
        private ID3D11Buffer _frameConstantBuffer;
        private ID3D11Query _frameCompletionQuery;
        private ID3D11SamplerState _linearClampSampler;
        private ID3D11SamplerState _linearZeroBorderSampler;
        private ID3D11RasterizerState _rasterizerState;

        public AppleMusicInspiredBackground(
            AppleMusicInspiredBackgroundSettings settings = null,
            bool lightTheme = false,
            Func<bool> isPlayingProvider = null,
            IntPtr compositionWindowHandle = default,
            Action firstCompositionFramePresented = null,
            Func<int> deviceLatencyProvider = null,
            Func<string, Task<Bitmap>> artworkLoader = null,
            int presetSlot = -1,
            Func<string> audioEndpointIdProvider = null)
        {
            _settings = settings ?? new();
            _isPlayingProvider = isPlayingProvider;
            _compositionWindowHandle = compositionWindowHandle;
            _firstCompositionFramePresented = firstCompositionFramePresented;
            _artworkLoader = artworkLoader;
            _spectrumAnalysis = new AppleMusicSpectrumAnalysis(
                deviceLatencyProvider,
                audioEndpointIdProvider);
            if (presetSlot < 0)
            {
                PresetIndex = AppleMusicInspiredMesh.SelectPreset();
                LandscapePresetIndex = AppleMusicInspiredMesh.SelectLandscapePreset();
            }
            else
            {
                int resolvedSlot = Math.Clamp(
                    presetSlot,
                    0,
                    AppleMusicInspiredMesh.PresetSlotCount - 1);
                PresetIndex = AppleMusicInspiredMesh.ResolvePortraitPreset(resolvedSlot);
                LandscapePresetIndex = resolvedSlot;
            }
            (_meshVertices, _meshIndices) = CreateMesh(_isVerticalLayout);

            double renderScale = _settings.RenderScale;
            _renderScale = double.IsFinite(renderScale) && renderScale > 0
                ? Math.Clamp(renderScale, 0.125d, 1d)
                : 1d;
            _lightTheme = lightTheme;
            int? frameRateLimit = _settings.FrameRateLimit;
            _refreshDisabled = frameRateLimit == 0;
            int? effectiveFrameRate = frameRateLimit < 0 ? 60 : frameRateLimit;
            _minimumRenderIntervalTicks = effectiveFrameRate > 0
                ? Math.Max(1, Stopwatch.Frequency / (long)effectiveFrameRate.Value)
                : 0;

            ClipToBounds = true;
            Background = System.Windows.Media.Brushes.Black;
            IsHitTestVisible = false;

            _d3DImage = new D3DImage();
            _d3DImage.IsFrontBufferAvailableChanged += OnFrontBufferAvailableChanged;
            _image = new System.Windows.Controls.Image
            {
                Source = _d3DImage,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            Children.Add(_image);

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnSizeChanged;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        public int PresetIndex { get; }

        public int LandscapePresetIndex { get; }

        private (AppleMusicPinchVertex[] Vertices, ushort[] Indices) CreateMesh(
            bool isVerticalLayout)
        {
            return AppleMusicInspiredMesh.Create(
                isVerticalLayout ? PresetIndex : LandscapePresetIndex,
                isVerticalLayout,
                isVerticalLayout
                    ? _settings.PortraitControlPointCount
                    : _settings.LandscapeControlPointCount,
                isVerticalLayout
                    ? _settings.PortraitSubdivisionLevels
                    : _settings.LandscapeSubdivisionLevels);
        }

        public void SetArtwork(string url, string trackId)
        {
            if (string.IsNullOrWhiteSpace(url) ||
                (string.Equals(_currentArtworkUrl, url, StringComparison.Ordinal) &&
                 string.Equals(_currentTrackId, trackId, StringComparison.Ordinal)))
            {
                return;
            }

            _currentArtworkUrl = url;
            _currentTrackId = trackId ?? string.Empty;
            int generation = ++_artworkGeneration;
            _ = LoadArtworkAsync(url, generation);
        }

        public async Task SetArtworkAsync(Bitmap source, string trackId)
        {
            if (source == null ||
                (string.Equals(_currentTrackId, trackId, StringComparison.Ordinal) &&
                 _currentArtworkData != null))
            {
                return;
            }

            _currentArtworkUrl = string.Empty;
            _currentTrackId = trackId ?? string.Empty;
            int generation = ++_artworkGeneration;

            try
            {
                ArtworkData artwork = await Task.Run(() => ArtworkData.FromBitmap(source));
                if (!Dispatcher.CheckAccess())
                {
                    await Dispatcher.InvokeAsync(() => ApplyArtwork(artwork, generation));
                }
                else
                {
                    ApplyArtwork(artwork, generation);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        public async Task SetArtworkAsync(BackgroundArtwork artwork)
        {
            if (artwork == null) throw new ArgumentNullException(nameof(artwork));
            using Bitmap bitmap = AppleMusicInspiredArtworkLoader.Decode(artwork.EncodedData);
            await SetArtworkAsync(bitmap, artwork.Id);
        }

        public void SetVerticalLayout(bool isVertical, bool animate = true)
        {
            bool orientationChanged = _isVerticalLayout != isVertical;
            if (orientationChanged)
            {
                _isVerticalLayout = isVertical;
                (_meshVertices, _meshIndices) = CreateMesh(_isVerticalLayout);
            }

            _forceNextRender = true;
            _nextRenderTimestamp = 0;
            if (IsLoaded)
            {
                try
                {
                    if (orientationChanged)
                    {
                        RecreatePinchMeshBuffers();
                    }
                    EnsureSurfaceSize();
                }
                catch (Exception ex)
                {
                    QueueDeviceRecovery(ex);
                }
            }
            InvalidateVisual();
        }

        public void SetPresentationVisible(bool visible)
        {
            if (_presentationVisible == visible)
            {
                return;
            }

            _presentationVisible = visible;
            _forceNextRender = true;
            _nextRenderTimestamp = 0;
            if (visible && IsLoaded && IsVisible)
            {
                HookRendering();
                InvalidateVisual();
            }
            else
            {
                UnhookRendering();
            }
        }

        /// <summary>
        /// Reopens audio capture using the endpoint currently returned by the
        /// endpoint provider supplied to the constructor.
        /// </summary>
        public void RefreshAudioEndpoint()
        {
            _spectrumAnalysis.RefreshAudioEndpoint();
        }

        /// <summary>
        /// Enables the visual treatment used behind lyrics.
        /// </summary>
        public void SetIsBehindLyrics(bool isBehindLyrics)
        {
            if (_isBehindLyrics == isBehindLyrics)
            {
                return;
            }

            double time = _animationClock.Elapsed.TotalSeconds;
            float currentMix = GetLyricsModeMix(time);
            float targetMix = isBehindLyrics ? 1f : 0f;
            _isBehindLyrics = isBehindLyrics;

            if (_refreshDisabled || !_animationClock.IsRunning)
            {
                _lyricsModeMix = targetMix;
                _lyricsModeMixFrom = targetMix;
                _lyricsModeMixTo = targetMix;
                _lyricsModeTransitioning = false;
            }
            else
            {
                _lyricsModeMix = currentMix;
                _lyricsModeMixFrom = currentMix;
                _lyricsModeMixTo = targetMix;
                _lyricsModeTransitionStartTime = time;
                _lyricsModeTransitioning = Math.Abs(targetMix - currentMix) > 0.0001f;
            }

            _forceNextRender = true;
            _nextRenderTimestamp = 0;
            InvalidateVisual();
        }

        private async Task LoadArtworkAsync(string url, int generation)
        {
            try
            {
                using Bitmap bitmap = _artworkLoader == null
                    ? AppleMusicInspiredArtworkLoader.Decode(
                        await AppleMusicInspiredArtworkLoader.LoadFromUriAsync(new Uri(url)))
                    : await _artworkLoader(url);
                if (bitmap == null || generation != _artworkGeneration)
                {
                    return;
                }

                ArtworkData artwork = ArtworkData.FromBitmap(bitmap);
                if (!Dispatcher.CheckAccess())
                {
                    await Dispatcher.InvokeAsync(() => ApplyArtwork(artwork, generation));
                }
                else
                {
                    ApplyArtwork(artwork, generation);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void ApplyArtwork(ArtworkData artwork, int generation)
        {
            if (generation != _artworkGeneration)
            {
                return;
            }

            _previousArtworkData = _currentArtworkData;
            _currentArtworkData = artwork;

            if (_device != null)
            {
                try
                {
                    GpuArtwork uploaded = UploadArtwork(artwork);
                    _previousArtwork?.Dispose();
                    _previousArtwork = _currentArtwork;
                    _currentArtwork = uploaded;
                }
                catch (Exception ex)
                {
                    QueueDeviceRecovery(ex);
                    return;
                }
            }

            bool hasPrevious = _previousArtworkData != null;
            _transitioning = hasPrevious && !_refreshDisabled;
            _transitionStartTime = _animationClock.Elapsed.TotalSeconds;
            if (!_transitioning)
            {
                _previousArtworkData = null;
                _previousArtwork?.Dispose();
                _previousArtwork = null;
            }

            _forceNextRender = true;
            _nextRenderTimestamp = 0;
            InvalidateVisual();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_device == null)
                {
                    InitializeDeviceResources();
                }
                EnsureSurfaceSize();
                _deviceRecoveryPending = false;
            }
            catch (Exception ex)
            {
                ReleaseDirectXResources();
                QueueDeviceRecovery(ex);
            }

            if (_presentationVisible && IsVisible)
            {
                HookRendering();
            }
            if (!_refreshDisabled)
            {
                _spectrumAnalysis.Start();
            }
            _forceNextRender = true;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UnhookRendering();
            _spectrumAnalysis.Stop();
            ReleaseDirectXResources();
            _deviceRecoveryPending = false;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded || _device == null || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            try
            {
                EnsureSurfaceSize();
            }
            catch (Exception ex)
            {
                QueueDeviceRecovery(ex);
            }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible && IsLoaded && _presentationVisible)
            {
                HookRendering();
                _forceNextRender = true;
            }
            else
            {
                UnhookRendering();
            }
        }

        private void OnFrontBufferAvailableChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
        {
            if (!IsLoaded || _reloading || !ReferenceEquals(sender, _d3DImage) ||
                !_d3DImage.IsFrontBufferAvailable || _sharedTexture9 == null)
            {
                return;
            }

            try
            {
                AttachBackBuffer(_d3DImage, _sharedTexture9);
                _forceNextRender = true;
            }
            catch (Exception ex)
            {
                QueueDeviceRecovery(ex);
            }
        }

        private void HookRendering()
        {
            if (_renderingHooked)
            {
                return;
            }
            _animationClock.Start();
            CompositionTarget.Rendering += OnRendering;
            _renderingHooked = true;
        }

        private void UnhookRendering()
        {
            if (!_renderingHooked)
            {
                return;
            }
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
            _animationClock.Stop();
        }

        private void OnRendering(object sender, EventArgs e)
        {
            if (_reloading || !IsLoaded || !IsVisible || !_presentationVisible)
            {
                return;
            }

            if (_deviceRecoveryPending)
            {
                ScheduleDeviceRecovery();
                return;
            }

            try
            {
                EnsureSurfaceSize();
            }
            catch (Exception ex)
            {
                QueueDeviceRecovery(ex);
                return;
            }

            if ((_compositionPresenter == null && !_d3DImage.IsFrontBufferAvailable) ||
                _device == null || _context == null || _outputSurface == null ||
                _currentArtwork == null || _frameConstantBuffer == null)
            {
                return;
            }

            if (_refreshDisabled && !_forceNextRender)
            {
                return;
            }

            long timestamp = Stopwatch.GetTimestamp();
            if (!_forceNextRender && _minimumRenderIntervalTicks > 0 &&
                _nextRenderTimestamp > timestamp)
            {
                return;
            }

            try
            {
                RenderFrame();
                CompleteRenderTiming(timestamp);
            }
            catch (Exception ex)
            {
                QueueDeviceRecovery(ex);
            }
        }

        private void CompleteRenderTiming(long timestamp)
        {
            bool forced = _forceNextRender;
            _forceNextRender = false;
            if (_minimumRenderIntervalTicks <= 0)
            {
                _nextRenderTimestamp = 0;
                return;
            }

            if (forced || _nextRenderTimestamp <= 0)
            {
                _nextRenderTimestamp = timestamp + _minimumRenderIntervalTicks;
                return;
            }

            if (_nextRenderTimestamp <= timestamp)
            {
                long elapsedIntervals =
                    (timestamp - _nextRenderTimestamp) / _minimumRenderIntervalTicks + 1;
                _nextRenderTimestamp += elapsedIntervals * _minimumRenderIntervalTicks;
            }
        }

        private void RenderFrame()
        {
            double time = _animationClock.Elapsed.TotalSeconds;
            float transitionMix = GetTransitionMix(time);
            float lyricsModeMix = GetLyricsModeMix(time);
            float viewAspectRatio = _outputSurface.Width / (float)_outputSurface.Height;
            Vector2 viewScale = viewAspectRatio >= 1f
                ? new Vector2(1f, viewAspectRatio)
                : new Vector2(1f / viewAspectRatio, 1f);
            float pinchTextureScale = _isVerticalLayout
                ? PortraitTextureScale
                : LandscapeTextureScale;
            float pinchTextureOffset = (1f - pinchTextureScale) * 0.5f;
            Vector4 lyricsImageScales = _refreshDisabled
                ? Vector4.One
                : _spectrumAnalysis.GetImageScales(
                    _isPlayingProvider?.Invoke() ??
                        false,
                    GetSettingScale(_settings.BassPulseScale));
            var pinchTextureTransform = new Vector4(
                pinchTextureScale,
                pinchTextureScale,
                pinchTextureOffset,
                pinchTextureOffset);
            float blurScale = GetSettingScale(_settings.BlurScale);
            // Interpolate the blur radius during mode transitions.
            float currentBlurSigma = Lerp(
                OrdinaryBlurSigma,
                LyricsBlurSigma,
                lyricsModeMix);
            var constants = new FrameConstants
            {
                Time = (float)time,
                TextureTransitionMix = transitionMix,
                ViewScale = viewScale,
                BlackScrimAlpha = _lightTheme
                    ? LightAppearanceBlackScrimAlpha
                    : DarkBehindLyricsBlackScrimAlpha,
                OutputDitherStrength = 1f,
                BlurScale = GetBlurScale(currentBlurSigma, blurScale),
                ImageScales = Vector4.One,
                PinchTextureTransform = pinchTextureTransform,
                LyricsModeMix = lyricsModeMix,
            };

            bool imageLocked = false;
            try
            {
                if (_compositionPresenter == null)
                {
                    _d3DImage.Lock();
                    imageLocked = true;
                }

                BindConstantBuffer();
                BindSampler();
                _context.RSSetState(_rasterizerState);

                ID3D11ShaderResourceView lyricsBackdrop;
                ID3D11ShaderResourceView ordinaryBackdrop;
                bool needsOrdinaryBackdrop = lyricsModeMix < 0.9999f;
                bool needsLyricsBackdrop = lyricsModeMix > 0.0001f;

                if (needsOrdinaryBackdrop && needsLyricsBackdrop)
                {
                    // Both modes keep the treated artwork while transitioning.
                    constants.ImageScales = Vector4.One;
                    _context.UpdateSubresource(in constants, _frameConstantBuffer);
                    RenderBackdropPass(_ordinaryBlurSurface);
                    ordinaryBackdrop = _ordinaryBlurSurface.ShaderResourceView;

                    constants.ImageScales = lyricsImageScales;
                    _context.UpdateSubresource(in constants, _frameConstantBuffer);
                    RenderBackdropPass(_verticalBlurSurface);
                    lyricsBackdrop = _verticalBlurSurface.ShaderResourceView;
                }
                else
                {
                    constants.ImageScales = needsLyricsBackdrop
                        ? lyricsImageScales
                        : Vector4.One;
                    _context.UpdateSubresource(in constants, _frameConstantBuffer);
                    RenderBackdropPass(_verticalBlurSurface);
                    lyricsBackdrop = _verticalBlurSurface.ShaderResourceView;
                    ordinaryBackdrop = lyricsBackdrop;
                }

                RenderCompositePass(
                    lyricsBackdrop,
                    ordinaryBackdrop,
                    lyricsModeMix);

                UnbindPixelShaderResources(2);
                SetRenderTarget(null);
                WaitForFrameCompletion();
                Result removedReason = _device.DeviceRemovedReason;
                if (removedReason.Failure)
                {
                    removedReason.CheckError();
                }

                if (_compositionPresenter != null)
                {
                    _compositionPresenter.Present(_context, _outputSurface.Texture);
                }
                else
                {
                    _d3DImage.AddDirtyRect(new Int32Rect(
                        0,
                        0,
                        _outputSurface.Width,
                        _outputSurface.Height));
                }
            }
            finally
            {
                if (imageLocked)
                {
                    _d3DImage.Unlock();
                }
            }

            if (_compositionPresenter == null)
            {
                _image.InvalidateVisual();
            }
        }

        private static float GetSettingScale(double value)
        {
            return double.IsFinite(value)
                ? (float)Math.Clamp(value, 0d, 10d)
                : 1f;
        }

        private Vector2 GetBlurScale(float sigma, float settingScale)
        {
            // Convert the blur radius to the downsampled surface dimensions.
            float targetOutputSigma =
                sigma * BlurSurfaceDownsample * settingScale * (float)_renderScale;
            return new Vector2(
                targetOutputSigma * _rotationSurface.Width /
                    (_outputSurface.Width * GaussianKernelSigma),
                targetOutputSigma * _rotationSurface.Height /
                    (_outputSurface.Height * GaussianKernelSigma));
        }

        private float GetLyricsModeMix(double time)
        {
            if (!_lyricsModeTransitioning)
            {
                return _lyricsModeMix;
            }

            float progress = (float)(
                (time - _lyricsModeTransitionStartTime) /
                LyricsModeTransitionSeconds);
            if (progress >= 1f)
            {
                _lyricsModeTransitioning = false;
                _lyricsModeMix = _lyricsModeMixTo;
                return _lyricsModeMix;
            }

            float easedProgress = EvaluateUIKitEaseInOut(Math.Clamp(progress, 0f, 1f));
            _lyricsModeMix = Lerp(
                _lyricsModeMixFrom,
                _lyricsModeMixTo,
                easedProgress);
            return _lyricsModeMix;
        }

        // Standard ease-in-out timing curve.
        private static float EvaluateUIKitEaseInOut(float progress)
        {
            if (progress <= 0f || progress >= 1f)
            {
                return progress;
            }

            float lower = 0f;
            float upper = 1f;
            float parameter = progress;
            for (int iteration = 0; iteration < 12; iteration++)
            {
                parameter = (lower + upper) * 0.5f;
                float inverse = 1f - parameter;
                float x =
                    3f * inverse * inverse * parameter * 0.42f +
                    3f * inverse * parameter * parameter * 0.58f +
                    parameter * parameter * parameter;
                if (x < progress)
                {
                    lower = parameter;
                }
                else
                {
                    upper = parameter;
                }
            }

            return parameter * parameter * (3f - 2f * parameter);
        }

        private static float Lerp(float from, float to, float amount)
        {
            return from + (to - from) * amount;
        }

        private float GetTransitionMix(double time)
        {
            if (!_transitioning || _previousArtwork == null)
            {
                return 1f;
            }

            float progress = (float)((time - _transitionStartTime) / ArtworkTransitionSeconds);
            if (progress < 1f)
            {
                return Math.Clamp(progress, 0f, 1f);
            }

            _transitioning = false;
            _previousArtworkData = null;
            _previousArtwork.Dispose();
            _previousArtwork = null;
            return 1f;
        }

        private void RenderBackdropPass(RenderSurface verticalBlurTarget)
        {
            RenderRotationPass();
            RenderBlurPass(
                _rotationSurface.ShaderResourceView,
                _horizontalBlurSurface,
                _horizontalBlurPixelShader);
            RenderBlurPass(
                _horizontalBlurSurface.ShaderResourceView,
                verticalBlurTarget,
                _verticalBlurPixelShader);
        }

        private void RenderRotationPass()
        {
            SetViewport(_rotationSurface.Width, _rotationSurface.Height);
            SetRenderTarget(_rotationSurface.RenderTargetView);
            _context.ClearRenderTargetView(
                _rotationSurface.RenderTargetView,
                new Color4(0f, 0f, 0f, 1f));
            _context.IASetInputLayout(_quadInputLayout);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            BindVertexBuffer(_quadVertexBuffer, Marshal.SizeOf<QuadVertex>());
            _context.IASetIndexBuffer(_quadIndexBuffer, Format.R16_UInt, 0);
            _context.VSSetShader(_rotationVertexShader, null, 0);
            _context.PSSetShader(_rotationPixelShader, null, 0);
            BindPixelShaderResources(
                _currentArtwork.ShaderResourceView,
                _previousArtwork?.ShaderResourceView ?? _currentArtwork.ShaderResourceView);

            // Keep an aspect-fill copy underneath the moving layers.
            _context.VSSetShader(_artworkFillVertexShader, null, 0);
            _context.DrawIndexed(6, 0, 0);

            _context.VSSetShader(_rotationVertexShader, null, 0);
            _context.DrawIndexedInstanced(6, 3, 0, 0, 0);
            UnbindPixelShaderResources(2);
        }

        private void RenderBlurPass(
            ID3D11ShaderResourceView source,
            RenderSurface target,
            ID3D11PixelShader shader)
        {
            SetViewport(target.Width, target.Height);
            SetRenderTarget(target.RenderTargetView);
            _context.IASetInputLayout(_quadInputLayout);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            BindVertexBuffer(_quadVertexBuffer, Marshal.SizeOf<QuadVertex>());
            _context.IASetIndexBuffer(_quadIndexBuffer, Format.R16_UInt, 0);
            _context.VSSetShader(_fullscreenVertexShader, null, 0);
            _context.PSSetShader(shader, null, 0);
            BindPixelShaderResources(source);
            _context.DrawIndexed(6, 0, 0);
            UnbindPixelShaderResources(1);
        }

        private void RenderCompositePass(
            ID3D11ShaderResourceView lyricsBackdrop,
            ID3D11ShaderResourceView ordinaryBackdrop,
            float lyricsModeMix)
        {
            SetViewport(_outputSurface.Width, _outputSurface.Height);
            SetRenderTarget(_outputSurface.RenderTargetView);
            _context.ClearRenderTargetView(
                _outputSurface.RenderTargetView,
                new Color4(0f, 0f, 0f, 1f));

            if (lyricsModeMix <= 0f)
            {
                // isBehindLyrics=false keeps the treated blurred artwork but
                // does not submit the lyric pinch mesh.
                BindPixelShaderResources(ordinaryBackdrop);
                DrawFullscreenMaterial(_ordinaryMaterialPixelShader);
                return;
            }

            if (lyricsModeMix >= 1f)
            {
                BindPixelShaderResources(lyricsBackdrop);
                // Keep a treated layer beneath gaps exposed by the moving mesh.
                DrawFullscreenMaterial(_materialTreatedPixelShader);
                DrawPinchMesh(_pinchPixelShader);
                return;
            }

            BindPixelShaderResources(lyricsBackdrop, ordinaryBackdrop);
            // Composite the fullscreen layer and warped lyric mesh.
            DrawFullscreenMaterial(_materialCompositePixelShader);
            DrawPinchMesh(_pinchCompositePixelShader);
        }

        private void DrawFullscreenMaterial(ID3D11PixelShader pixelShader)
        {
            _context.IASetInputLayout(_quadInputLayout);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            BindVertexBuffer(_quadVertexBuffer, Marshal.SizeOf<QuadVertex>());
            _context.IASetIndexBuffer(_quadIndexBuffer, Format.R16_UInt, 0);
            _context.VSSetShader(_fullscreenVertexShader, null, 0);
            _context.PSSetShader(pixelShader, null, 0);
            _context.DrawIndexed(6, 0, 0);
        }

        private void DrawPinchMesh(ID3D11PixelShader pixelShader)
        {
            _context.IASetInputLayout(_pinchInputLayout);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            BindVertexBuffer(_pinchVertexBuffer, Marshal.SizeOf<AppleMusicPinchVertex>());
            _context.IASetIndexBuffer(_pinchIndexBuffer, Format.R16_UInt, 0);
            _context.VSSetShader(_pinchVertexShader, null, 0);
            _context.PSSetShader(pixelShader, null, 0);
            _context.DrawIndexed(_meshIndices.Length, 0, 0);
        }

        private void InitializeDeviceResources()
        {
            D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                [D3D11.GetSupportedFeatureLevel()],
                out ID3D11Device device);
            _device = device;
            _context = device.ImmediateContext;

            if (_compositionWindowHandle != IntPtr.Zero)
            {
                _compositionPresenter = new CompositionSwapChainPresenter(
                    device,
                    _compositionWindowHandle,
                    _firstCompositionFramePresented);
            }

            if (_compositionPresenter == null)
            {
                var presentParameters = new Vortice.Direct3D9.PresentParameters
                {
                    Windowed = true,
                    SwapEffect = Vortice.Direct3D9.SwapEffect.Discard,
                    DeviceWindowHandle = GetDesktopWindow(),
                    PresentationInterval = PresentInterval.Default,
                };
                _direct3D9 = D3D9.Direct3DCreate9Ex();
                _device9 = _direct3D9.CreateDeviceEx(
                    0,
                    DeviceType.Hardware,
                    IntPtr.Zero,
                    CreateFlags.HardwareVertexProcessing |
                    CreateFlags.Multithreaded |
                    CreateFlags.FpuPreserve,
                    presentParameters);
            }

            CreatePipelineResources();
            RecreateArtworkTextures();
        }

        private void CreatePipelineResources()
        {
            string shaderSource = ReadShaderSource();
            byte[] rotationVertex = CompileShader(shaderSource, "RotationVertex", "vs_5_0");
            byte[] artworkFillVertex = CompileShader(shaderSource, "ArtworkFillVertex", "vs_5_0");
            byte[] fullscreenVertex = CompileShader(shaderSource, "FullscreenVertex", "vs_5_0");
            byte[] pinchVertex = CompileShader(shaderSource, "PinchVertex", "vs_5_0");
            byte[] rotationPixel = CompileShader(shaderSource, "RotationPixel", "ps_5_0");
            byte[] horizontalBlurPixel = CompileShader(shaderSource, "BlurHorizontalPixel", "ps_5_0");
            byte[] verticalBlurPixel = CompileShader(shaderSource, "BlurVerticalPixel", "ps_5_0");
            byte[] ordinaryMaterialPixel = CompileShader(
                shaderSource,
                "OrdinaryMaterialPixel",
                "ps_5_0");
            byte[] materialTreatedPixel = CompileShader(
                shaderSource,
                "MaterialTreatedPixel",
                "ps_5_0");
            byte[] materialCompositePixel = CompileShader(
                shaderSource,
                "MaterialCompositePixel",
                "ps_5_0");
            byte[] pinchPixel = CompileShader(shaderSource, "PinchPixel", "ps_5_0");
            byte[] pinchCompositePixel = CompileShader(
                shaderSource,
                "PinchCompositePixel",
                "ps_5_0");

            _rotationVertexShader = CreateVertexShader(rotationVertex);
            _artworkFillVertexShader = CreateVertexShader(artworkFillVertex);
            _fullscreenVertexShader = CreateVertexShader(fullscreenVertex);
            _pinchVertexShader = CreateVertexShader(pinchVertex);
            _rotationPixelShader = CreatePixelShader(rotationPixel);
            _horizontalBlurPixelShader = CreatePixelShader(horizontalBlurPixel);
            _verticalBlurPixelShader = CreatePixelShader(verticalBlurPixel);
            _ordinaryMaterialPixelShader = CreatePixelShader(ordinaryMaterialPixel);
            _materialTreatedPixelShader = CreatePixelShader(materialTreatedPixel);
            _materialCompositePixelShader = CreatePixelShader(materialCompositePixel);
            _pinchPixelShader = CreatePixelShader(pinchPixel);
            _pinchCompositePixelShader = CreatePixelShader(pinchCompositePixel);

            _quadInputLayout = _device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 16, 0),
            ],
                rotationVertex);
            _pinchInputLayout = _device.CreateInputLayout(
            [
                new InputElementDescription("FROMPOS", 0, Format.R32G32_Float, 0, 0),
                new InputElementDescription("TOPOS", 0, Format.R32G32_Float, 8, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 16, 0),
            ],
                pinchVertex);

            QuadVertex[] quadVertices =
            [
                new(new Vector4(-1f, -1f, 0f, 1f), new Vector2(0f, 1f)),
                new(new Vector4(-1f, 1f, 0f, 1f), new Vector2(0f, 0f)),
                new(new Vector4(1f, 1f, 0f, 1f), new Vector2(1f, 0f)),
                new(new Vector4(1f, -1f, 0f, 1f), new Vector2(1f, 1f)),
            ];
            ushort[] quadIndices = [0, 1, 2, 2, 3, 0];
            _quadVertexBuffer = _device.CreateBuffer(quadVertices, BindFlags.VertexBuffer);
            _quadIndexBuffer = _device.CreateBuffer(quadIndices, BindFlags.IndexBuffer);
            _pinchVertexBuffer = _device.CreateBuffer(_meshVertices, BindFlags.VertexBuffer);
            _pinchIndexBuffer = _device.CreateBuffer(_meshIndices, BindFlags.IndexBuffer);
            _frameConstantBuffer = _device.CreateBuffer(
                Marshal.SizeOf<FrameConstants>(),
                BindFlags.ConstantBuffer,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.None,
                0);
            _frameCompletionQuery = _device.CreateQuery(
                new QueryDescription(
                    Vortice.Direct3D11.QueryType.Event,
                    Vortice.Direct3D11.QueryFlags.None));

            _linearClampSampler = _device.CreateSamplerState(new SamplerDescription(
                Filter.MinMagMipLinear,
                TextureAddressMode.Clamp,
                0f,
                1,
                ComparisonFunction.Never,
                0f,
                float.MaxValue));
            _linearZeroBorderSampler = _device.CreateSamplerState(
                new SamplerDescription
                {
                    Filter = Filter.MinMagMipLinear,
                    AddressU = TextureAddressMode.Border,
                    AddressV = TextureAddressMode.Border,
                    AddressW = TextureAddressMode.Border,
                    MipLODBias = 0f,
                    MaxAnisotropy = 1,
                    ComparisonFunc = ComparisonFunction.Never,
                    BorderColor = new Color4(0f, 0f, 0f, 0f),
                    MinLOD = 0f,
                    MaxLOD = float.MaxValue,
                });
            _rasterizerState = _device.CreateRasterizerState(
                new RasterizerDescription(
                    CullMode.None,
                    Vortice.Direct3D11.FillMode.Solid));
        }

        private void RecreatePinchMeshBuffers()
        {
            if (_device == null)
            {
                return;
            }

            ID3D11Buffer replacementVertices = _device.CreateBuffer(
                _meshVertices,
                BindFlags.VertexBuffer);
            ID3D11Buffer replacementIndices;
            try
            {
                replacementIndices = _device.CreateBuffer(
                    _meshIndices,
                    BindFlags.IndexBuffer);
            }
            catch
            {
                replacementVertices.Dispose();
                throw;
            }

            ID3D11Buffer previousVertices = _pinchVertexBuffer;
            ID3D11Buffer previousIndices = _pinchIndexBuffer;
            _pinchVertexBuffer = replacementVertices;
            _pinchIndexBuffer = replacementIndices;
            previousVertices?.Dispose();
            previousIndices?.Dispose();
        }

        private void RecreateArtworkTextures()
        {
            _currentArtwork?.Dispose();
            _currentArtwork = null;
            _previousArtwork?.Dispose();
            _previousArtwork = null;

            if (_currentArtworkData != null)
            {
                _currentArtwork = UploadArtwork(_currentArtworkData);
            }

            if (_transitioning && _previousArtworkData != null &&
                _animationClock.Elapsed.TotalSeconds - _transitionStartTime <
                ArtworkTransitionSeconds)
            {
                _previousArtwork = UploadArtwork(_previousArtworkData);
            }
            else
            {
                _transitioning = false;
                _previousArtworkData = null;
            }
        }

        private GpuArtwork UploadArtwork(ArtworkData artwork)
        {
            var description = new Texture2DDescription
            {
                Width = artwork.Width,
                Height = artwork.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None,
            };

            ID3D11Texture2D texture;
            unsafe
            {
                fixed (byte* pixels = artwork.Pixels)
                {
                    var initialData = new SubresourceData(
                        (IntPtr)pixels,
                        artwork.Width * 4,
                        artwork.Width * artwork.Height * 4);
                    texture = _device.CreateTexture2D(description, initialData);
                }
            }
            ID3D11ShaderResourceView view = null;
            try
            {
                view = _device.CreateShaderResourceView(texture);
                return new GpuArtwork(texture, view);
            }
            catch
            {
                view?.Dispose();
                texture.Dispose();
                throw;
            }
        }

        private (int Width, int Height) GetPhysicalPixelSize(DpiScale dpi)
        {
            return (
                Math.Max(1, (int)Math.Round(
                    ActualWidth * dpi.DpiScaleX * _renderScale,
                    MidpointRounding.AwayFromZero)),
                Math.Max(1, (int)Math.Round(
                    ActualHeight * dpi.DpiScaleY * _renderScale,
                    MidpointRounding.AwayFromZero)));
        }

        private void EnsureSurfaceSize()
        {
            if (_device == null || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            if (_compositionPresenter == null)
            {
                EnsureD3DImageDpi(dpi);
            }
            var size = GetPhysicalPixelSize(dpi);
            if (_outputSurface != null &&
                _outputSurface.Width == size.Width &&
                _outputSurface.Height == size.Height)
            {
                return;
            }
            CreateRenderSurfaces(size.Width, size.Height);
        }

        private void EnsureD3DImageDpi(DpiScale dpi)
        {
            double dpiX = dpi.PixelsPerInchX * _renderScale;
            double dpiY = dpi.PixelsPerInchY * _renderScale;
            if (Math.Abs(_d3DImageDpiX - dpiX) < 0.01 &&
                Math.Abs(_d3DImageDpiY - dpiY) < 0.01)
            {
                return;
            }

            D3DImage previousImage = _d3DImage;
            var replacement = new D3DImage(dpiX, dpiY);
            replacement.IsFrontBufferAvailableChanged += OnFrontBufferAvailableChanged;
            if (_sharedTexture9 != null)
            {
                AttachBackBuffer(replacement, _sharedTexture9);
            }

            _d3DImage = replacement;
            _image.Source = replacement;
            _d3DImageDpiX = dpiX;
            _d3DImageDpiY = dpiY;
            previousImage.IsFrontBufferAvailableChanged -= OnFrontBufferAvailableChanged;
            TryDetachBackBuffer(previousImage);
            _forceNextRender = true;
        }

        private void CreateRenderSurfaces(int width, int height)
        {
            RenderSurface newRotation = null;
            RenderSurface newHorizontalBlur = null;
            RenderSurface newVerticalBlur = null;
            RenderSurface newOrdinaryBlur = null;
            RenderSurface newOutput = null;
            IDirect3DTexture9 newSharedTexture9 = null;
            _reloading = true;
            try
            {
                // Downsample for the widest blur while keeping adjacent taps.
                double maximumKernelScale =
                    Math.Max(LyricsBlurSigma, OrdinaryBlurSigma) /
                    GaussianKernelSigma *
                    GetSettingScale(_settings.BlurScale);
                double backdropDownsample =
                    BlurSurfaceDownsample * Math.Max(1d, maximumKernelScale);
                int backdropWidth =
                    Math.Max(1, (int)Math.Floor(width / backdropDownsample));
                int backdropHeight =
                    Math.Max(1, (int)Math.Floor(height / backdropDownsample));

                // Preserve the extended color range until the BGRA8 output pass.
                newRotation = CreateSurface(
                    backdropWidth,
                    backdropHeight,
                    Format.R16G16B16A16_Float,
                    true);
                newHorizontalBlur = CreateSurface(
                    backdropWidth,
                    backdropHeight,
                    Format.R16G16B16A16_Float,
                    true);
                newVerticalBlur = CreateSurface(
                    backdropWidth,
                    backdropHeight,
                    Format.R16G16B16A16_Float,
                    true);
                newOrdinaryBlur = CreateSurface(
                    backdropWidth,
                    backdropHeight,
                    Format.R16G16B16A16_Float,
                    true);
                bool usesComposition = _compositionPresenter != null;
                newOutput = CreateSurface(
                    width,
                    height,
                    Format.B8G8R8A8_UNorm,
                    false,
                    !usesComposition);

                if (usesComposition)
                {
                    DpiScale dpi = VisualTreeHelper.GetDpi(this);
                    _compositionPresenter.EnsureSize(
                        width,
                        height,
                        (float)(ActualWidth * dpi.DpiScaleX / width),
                        (float)(ActualHeight * dpi.DpiScaleY / height));
                }
                else
                {
                    IntPtr handle = GetSharedHandle(newOutput.Texture);
                    newSharedTexture9 = _device9.CreateTexture(
                        width,
                        height,
                        1,
                        Vortice.Direct3D9.Usage.RenderTarget,
                        Vortice.Direct3D9.Format.A8R8G8B8,
                        Pool.Default,
                        ref handle);
                    AttachBackBuffer(_d3DImage, newSharedTexture9);
                }

                RenderSurface oldRotation = _rotationSurface;
                RenderSurface oldHorizontalBlur = _horizontalBlurSurface;
                RenderSurface oldVerticalBlur = _verticalBlurSurface;
                RenderSurface oldOrdinaryBlur = _ordinaryBlurSurface;
                RenderSurface oldOutput = _outputSurface;
                IDirect3DTexture9 oldSharedTexture9 = _sharedTexture9;

                _rotationSurface = newRotation;
                _horizontalBlurSurface = newHorizontalBlur;
                _verticalBlurSurface = newVerticalBlur;
                _ordinaryBlurSurface = newOrdinaryBlur;
                _outputSurface = newOutput;
                _sharedTexture9 = newSharedTexture9;
                newRotation = null;
                newHorizontalBlur = null;
                newVerticalBlur = null;
                newOrdinaryBlur = null;
                newOutput = null;
                newSharedTexture9 = null;

                oldSharedTexture9?.Dispose();
                oldOutput?.Dispose();
                oldOrdinaryBlur?.Dispose();
                oldVerticalBlur?.Dispose();
                oldHorizontalBlur?.Dispose();
                oldRotation?.Dispose();
                _forceNextRender = true;
                _nextRenderTimestamp = 0;
            }
            finally
            {
                newSharedTexture9?.Dispose();
                newOutput?.Dispose();
                newOrdinaryBlur?.Dispose();
                newVerticalBlur?.Dispose();
                newHorizontalBlur?.Dispose();
                newRotation?.Dispose();
                _reloading = false;
            }
        }

        private RenderSurface CreateSurface(
            int width,
            int height,
            Format format,
            bool createShaderResource,
            bool shared = false)
        {
            var description = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget |
                    (createShaderResource ? BindFlags.ShaderResource : BindFlags.None),
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = shared ? ResourceOptionFlags.Shared : ResourceOptionFlags.None,
            };
            ID3D11Texture2D texture = _device.CreateTexture2D(description);
            ID3D11RenderTargetView renderTarget = null;
            ID3D11ShaderResourceView shaderResource = null;
            try
            {
                renderTarget = _device.CreateRenderTargetView(texture);
                if (createShaderResource)
                {
                    shaderResource = _device.CreateShaderResourceView(texture);
                }
                return new RenderSurface(texture, renderTarget, shaderResource, width, height);
            }
            catch
            {
                shaderResource?.Dispose();
                renderTarget?.Dispose();
                texture.Dispose();
                throw;
            }
        }

        private static IntPtr GetSharedHandle(ID3D11Texture2D texture)
        {
            using IDXGIResource resource = texture.QueryInterface<IDXGIResource>();
            return resource.SharedHandle;
        }

        private static void AttachBackBuffer(D3DImage image, IDirect3DTexture9 texture)
        {
            using IDirect3DSurface9 surface = texture.GetSurfaceLevel(0);
            image.Lock();
            try
            {
                image.SetBackBuffer(
                    D3DResourceType.IDirect3DSurface9,
                    surface.NativePointer,
                    enableSoftwareFallback: true);
            }
            finally
            {
                image.Unlock();
            }
        }

        private static void TryDetachBackBuffer(D3DImage image)
        {
            if (image == null || image.PixelWidth <= 0)
            {
                return;
            }
            try
            {
                image.Lock();
                try
                {
                    image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
                }
                finally
                {
                    image.Unlock();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void QueueDeviceRecovery(Exception exception)
        {
            Debug.WriteLine(exception);
            _deviceRecoveryPending = true;
            ScheduleDeviceRecovery();
        }

        private void ScheduleDeviceRecovery()
        {
            if (_deviceRecoveryScheduled || !IsLoaded ||
                Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            _deviceRecoveryScheduled = true;
            _ = Dispatcher.BeginInvoke(
                new Action(TryRecoverDevice),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void TryRecoverDevice()
        {
            _deviceRecoveryScheduled = false;
            if (!_deviceRecoveryPending || !IsLoaded)
            {
                return;
            }

            try
            {
                ReleaseDirectXResources();
                InitializeDeviceResources();
                EnsureSurfaceSize();
                _deviceRecoveryPending = false;
                _forceNextRender = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                ReleaseDirectXResources();
                _deviceRecoveryPending = true;
            }
        }

        private void ReleaseDirectXResources()
        {
            _reloading = true;
            try
            {
                TryDetachBackBuffer(_d3DImage);
                try
                {
                    _context?.ClearState();
                    _context?.Flush();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }

                _currentArtwork?.Dispose();
                _currentArtwork = null;
                _previousArtwork?.Dispose();
                _previousArtwork = null;

                _sharedTexture9?.Dispose();
                _sharedTexture9 = null;
                _outputSurface?.Dispose();
                _outputSurface = null;
                _ordinaryBlurSurface?.Dispose();
                _ordinaryBlurSurface = null;
                _verticalBlurSurface?.Dispose();
                _verticalBlurSurface = null;
                _horizontalBlurSurface?.Dispose();
                _horizontalBlurSurface = null;
                _rotationSurface?.Dispose();
                _rotationSurface = null;
                _compositionPresenter?.Dispose();
                _compositionPresenter = null;

                _rasterizerState?.Dispose();
                _rasterizerState = null;
                _linearZeroBorderSampler?.Dispose();
                _linearZeroBorderSampler = null;
                _linearClampSampler?.Dispose();
                _linearClampSampler = null;
                _frameCompletionQuery?.Dispose();
                _frameCompletionQuery = null;
                _frameConstantBuffer?.Dispose();
                _frameConstantBuffer = null;
                _pinchIndexBuffer?.Dispose();
                _pinchIndexBuffer = null;
                _pinchVertexBuffer?.Dispose();
                _pinchVertexBuffer = null;
                _quadIndexBuffer?.Dispose();
                _quadIndexBuffer = null;
                _quadVertexBuffer?.Dispose();
                _quadVertexBuffer = null;
                _pinchInputLayout?.Dispose();
                _pinchInputLayout = null;
                _quadInputLayout?.Dispose();
                _quadInputLayout = null;
                _pinchCompositePixelShader?.Dispose();
                _pinchCompositePixelShader = null;
                _pinchPixelShader?.Dispose();
                _pinchPixelShader = null;
                _materialCompositePixelShader?.Dispose();
                _materialCompositePixelShader = null;
                _materialTreatedPixelShader?.Dispose();
                _materialTreatedPixelShader = null;
                _ordinaryMaterialPixelShader?.Dispose();
                _ordinaryMaterialPixelShader = null;
                _verticalBlurPixelShader?.Dispose();
                _verticalBlurPixelShader = null;
                _horizontalBlurPixelShader?.Dispose();
                _horizontalBlurPixelShader = null;
                _rotationPixelShader?.Dispose();
                _rotationPixelShader = null;
                _pinchVertexShader?.Dispose();
                _pinchVertexShader = null;
                _fullscreenVertexShader?.Dispose();
                _fullscreenVertexShader = null;
                _artworkFillVertexShader?.Dispose();
                _artworkFillVertexShader = null;
                _rotationVertexShader?.Dispose();
                _rotationVertexShader = null;

                _device9?.Dispose();
                _device9 = null;
                _direct3D9?.Dispose();
                _direct3D9 = null;
                _context?.Dispose();
                _context = null;
                _device?.Dispose();
                _device = null;
            }
            finally
            {
                _reloading = false;
            }
        }

        private unsafe ID3D11VertexShader CreateVertexShader(byte[] bytecode)
        {
            fixed (byte* pointer = bytecode)
            {
                return _device.CreateVertexShader(pointer, bytecode.Length, null);
            }
        }

        private unsafe ID3D11PixelShader CreatePixelShader(byte[] bytecode)
        {
            fixed (byte* pointer = bytecode)
            {
                return _device.CreatePixelShader(pointer, bytecode.Length, null);
            }
        }

        private void SetRenderTarget(ID3D11RenderTargetView renderTarget)
        {
            if (renderTarget == null)
            {
                _context.OMSetRenderTargets(
                    0,
                    Array.Empty<ID3D11RenderTargetView>(),
                    null);
                return;
            }
            _context.OMSetRenderTargets(1, [renderTarget], null);
        }

        private void WaitForFrameCompletion()
        {
            // Flush only submits the command list; it does not guarantee that
            // the shared D3D9 surface is complete when D3DImage unlocks it.
            // An event query prevents WPF from copying partially rasterized
            // tiles during expensive large-surface frames.
            _context.End(_frameCompletionQuery);
            _context.Flush();
            while (true)
            {
                Result result = _context.GetData(
                    _frameCompletionQuery,
                    IntPtr.Zero,
                    0,
                    AsyncGetDataFlags.DoNotFlush);
                if (result == Result.Ok)
                {
                    return;
                }
                if (result.Failure)
                {
                    result.CheckError();
                }
                Thread.Yield();
            }
        }

        private void SetViewport(int width, int height)
        {
            var viewport = new Vortice.Mathematics.Viewport(width, height);
            _context.RSSetViewports([viewport]);
        }

        private void BindVertexBuffer(ID3D11Buffer buffer, int stride)
        {
            _context.IASetVertexBuffers(0, 1, [buffer], [stride], [0]);
        }

        private void BindConstantBuffer()
        {
            _context.VSSetConstantBuffers(0, 1, [_frameConstantBuffer]);
            _context.PSSetConstantBuffers(0, 1, [_frameConstantBuffer]);
        }

        private void BindSampler()
        {
            _context.PSSetSamplers(
                0,
                2,
                [_linearClampSampler, _linearZeroBorderSampler]);
        }

        private void BindPixelShaderResources(params ID3D11ShaderResourceView[] resources)
        {
            _context.PSSetShaderResources(0, resources.Length, resources);
        }

        private void UnbindPixelShaderResources(int count)
        {
            _context.PSSetShaderResources(
                0,
                count,
                new ID3D11ShaderResourceView[count]);
        }

        private static string ReadShaderSource()
        {
            Stream stream = typeof(AppleMusicInspiredBackgroundSettings).Assembly
                .GetManifestResourceStream(ShaderResourceName);
            if (stream == null)
            {
                throw new FileNotFoundException(ShaderResourceName);
            }
            using (stream)
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }

        private static byte[] CompileShader(string source, string entryPoint, string target)
        {
            byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
            IntPtr code = IntPtr.Zero;
            IntPtr errors = IntPtr.Zero;
            int result = D3DCompile(
                sourceBytes,
                (nuint)sourceBytes.Length,
                "AppleMusicInspiredBackground.hlsl",
                IntPtr.Zero,
                IntPtr.Zero,
                entryPoint,
                target,
                0x00000800u | 0x00008000u,
                0,
                out code,
                out errors);
            try
            {
                if (result < 0)
                {
                    string message = errors == IntPtr.Zero
                        ? $"D3DCompile failed for {entryPoint} (0x{result:X8})."
                        : Encoding.UTF8.GetString(CopyBlob(errors)).TrimEnd('\0');
                    throw new InvalidOperationException(message);
                }
                return CopyBlob(code);
            }
            finally
            {
                if (errors != IntPtr.Zero)
                {
                    Marshal.Release(errors);
                }
                if (code != IntPtr.Zero)
                {
                    Marshal.Release(code);
                }
            }
        }

        private static byte[] CopyBlob(IntPtr blob)
        {
            IntPtr vtable = Marshal.ReadIntPtr(blob);
            var getPointer = Marshal.GetDelegateForFunctionPointer<BlobGetBufferPointer>(
                Marshal.ReadIntPtr(vtable, IntPtr.Size * 3));
            var getSize = Marshal.GetDelegateForFunctionPointer<BlobGetBufferSize>(
                Marshal.ReadIntPtr(vtable, IntPtr.Size * 4));
            IntPtr buffer = getPointer(blob);
            nuint nativeSize = getSize(blob);
            if (nativeSize > int.MaxValue)
            {
                throw new InvalidOperationException("Compiled shader is unexpectedly large.");
            }
            var result = new byte[(int)nativeSize];
            Marshal.Copy(buffer, result, 0, result.Length);
            return result;
        }

        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(
            [In] byte[] sourceData,
            nuint sourceDataSize,
            string sourceName,
            IntPtr defines,
            IntPtr include,
            string entryPoint,
            string target,
            uint flags1,
            uint flags2,
            out IntPtr code,
            out IntPtr errorMessages);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr BlobGetBufferPointer(IntPtr blob);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate nuint BlobGetBufferSize(IntPtr blob);

        [StructLayout(LayoutKind.Sequential)]
        private struct FrameConstants
        {
            public float Time;
            public float TextureTransitionMix;
            public Vector2 ViewScale;
            public float BlackScrimAlpha;
            public float OutputDitherStrength;
            public Vector2 BlurScale;
            public Vector4 ImageScales;
            public Vector4 PinchTextureTransform;
            public float LyricsModeMix;
            public Vector3 Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct QuadVertex
        {
            public QuadVertex(Vector4 position, Vector2 textureCoordinate)
            {
                Position = position;
                TextureCoordinate = textureCoordinate;
            }

            public readonly Vector4 Position;
            public readonly Vector2 TextureCoordinate;
        }

        private sealed class ArtworkData
        {
            private const int ResizePixelThreshold = 100_000;
            private const int MaximumArtworkDimension = 300;

            private ArtworkData(byte[] pixels, int width, int height)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
            }

            public byte[] Pixels { get; }
            public int Width { get; }
            public int Height { get; }
            public static ArtworkData FromBitmap(Bitmap source)
            {
                if (source.Width <= 0 || source.Height <= 0)
                {
                    throw new ArgumentException("Artwork bitmap has no pixels.", nameof(source));
                }

                int width = source.Width;
                int height = source.Height;
                if ((long)width * height > ResizePixelThreshold)
                {
                    double aspectRatio = width / (double)height;
                    if (aspectRatio < 1d)
                    {
                        width = Math.Max(1, (int)(aspectRatio * MaximumArtworkDimension));
                        height = MaximumArtworkDimension;
                    }
                    else
                    {
                        width = MaximumArtworkDimension;
                        height = Math.Max(1, (int)(MaximumArtworkDimension / aspectRatio));
                    }
                }

                using var converted = new Bitmap(
                    width,
                    height,
                    DrawingPixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(converted))
                {
                    graphics.DrawImage(
                        source,
                        new Rectangle(0, 0, converted.Width, converted.Height),
                        0,
                        0,
                        source.Width,
                        source.Height,
                        GraphicsUnit.Pixel);
                }

                var rectangle = new Rectangle(0, 0, converted.Width, converted.Height);
                BitmapData data = converted.LockBits(
                    rectangle,
                    ImageLockMode.ReadOnly,
                    DrawingPixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes = converted.Width * 4;
                    var pixels = new byte[rowBytes * converted.Height];
                    for (int row = 0; row < converted.Height; row++)
                    {
                        Marshal.Copy(
                            IntPtr.Add(data.Scan0, row * data.Stride),
                            pixels,
                            row * rowBytes,
                            rowBytes);
                    }

                    return new ArtworkData(
                        pixels,
                        converted.Width,
                        converted.Height);
                }
                finally
                {
                    converted.UnlockBits(data);
                }
            }
        }

        private sealed class GpuArtwork : IDisposable
        {
            public GpuArtwork(ID3D11Texture2D texture, ID3D11ShaderResourceView shaderResourceView)
            {
                Texture = texture;
                ShaderResourceView = shaderResourceView;
            }

            public ID3D11Texture2D Texture { get; }
            public ID3D11ShaderResourceView ShaderResourceView { get; }

            public void Dispose()
            {
                ShaderResourceView.Dispose();
                Texture.Dispose();
            }
        }

        private sealed class RenderSurface : IDisposable
        {
            public RenderSurface(
                ID3D11Texture2D texture,
                ID3D11RenderTargetView renderTargetView,
                ID3D11ShaderResourceView shaderResourceView,
                int width,
                int height)
            {
                Texture = texture;
                RenderTargetView = renderTargetView;
                ShaderResourceView = shaderResourceView;
                Width = width;
                Height = height;
            }

            public ID3D11Texture2D Texture { get; }
            public ID3D11RenderTargetView RenderTargetView { get; }
            public ID3D11ShaderResourceView ShaderResourceView { get; }
            public int Width { get; }
            public int Height { get; }

            public void Dispose()
            {
                ShaderResourceView?.Dispose();
                RenderTargetView.Dispose();
                Texture.Dispose();
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();
    }
}
