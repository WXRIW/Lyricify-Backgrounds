using Lyricify.Backgrounds.Hosting.Wpf;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;

namespace Lyricify.Backgrounds.AppleMusicInspired.Wpf
{
    public sealed class IndependentAppleMusicInspiredBackground : FrameworkElement, IBackgroundSession
    {
        private readonly AppleMusicInspiredBackgroundSettings settings;
        private readonly bool lightTheme;
        private readonly Func<int>? deviceLatencyProvider;
        private readonly Func<string, Task<Bitmap>>? artworkLoader;
        private IndependentWpfBackgroundHost<BackgroundMessage>? renderHost;
        private string? artworkUrl;
        private string trackId = string.Empty;
        private Bitmap? artworkBitmap;
        private bool isVertical;
        private bool isPlaying;
        private bool isReady;
        private bool disposed;

        public IndependentAppleMusicInspiredBackground(
            AppleMusicInspiredBackgroundSettings? settings = null,
            bool lightTheme = false,
            Func<int>? deviceLatencyProvider = null,
            Func<string, Task<Bitmap>>? artworkLoader = null)
        {
            this.settings = settings?.Clone() ?? new AppleMusicInspiredBackgroundSettings();
            this.lightTheme = lightTheme;
            this.deviceLatencyProvider = deviceLatencyProvider;
            this.artworkLoader = artworkLoader;
            IsHitTestVisible = false;
            Loaded += OnLoaded;
            Unloaded += (_, _) => Dispose();
        }

        public event EventHandler? Ready;
        public event EventHandler<BackgroundFaultedEventArgs>? Faulted;
        public event EventHandler? FirstFramePresented
        {
            add => Ready += value;
            remove => Ready -= value;
        }

        public bool IsReady => isReady;

        public void SetArtwork(string url, string id)
        {
            artworkUrl = url;
            trackId = id ?? string.Empty;
            artworkBitmap?.Dispose();
            artworkBitmap = null;
            Post(BackgroundMessage.ForArtwork(url, trackId));
        }

        public void SetArtwork(Bitmap bitmap, string id)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
            artworkUrl = null;
            trackId = id ?? string.Empty;
            artworkBitmap?.Dispose();
            artworkBitmap = new Bitmap(bitmap);
            Post(BackgroundMessage.ForArtwork(new Bitmap(bitmap), trackId));
        }

        public void SetVerticalLayout(bool vertical)
        {
            if (isVertical == vertical) return;
            isVertical = vertical;
            Post(new BackgroundMessage { Kind = MessageKind.Layout, IsVertical = vertical });
        }

        public void SetPlaying(bool playing)
        {
            if (isPlaying == playing) return;
            isPlaying = playing;
            Post(new BackgroundMessage { Kind = MessageKind.Playback, IsPlaying = playing });
        }

        public void UpdateState(BackgroundState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            SetPlaying(state.IsPlaying);
            SetVerticalLayout(state.IsVertical);
        }

        public Task SetArtworkAsync(
            BackgroundArtwork artwork,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Bitmap bitmap = AppleMusicInspiredArtworkLoader.Decode(artwork.EncodedData);
            SetArtwork(bitmap, artwork.Id);
            return Task.CompletedTask;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (renderHost != null || disposed) return;
            Window owner = Window.GetWindow(this);
            if (owner == null)
            {
                Faulted?.Invoke(this, new BackgroundFaultedEventArgs(
                    new InvalidOperationException("The independent background must be loaded in a Window.")));
                return;
            }

            var initial = new BackgroundMessage
            {
                Kind = MessageKind.Initialize,
                Settings = settings,
                LightTheme = lightTheme,
                IsVertical = isVertical,
                IsPlaying = isPlaying,
                ArtworkUrl = artworkUrl,
                TrackId = trackId,
                ArtworkBitmap = artworkBitmap == null ? null : new Bitmap(artworkBitmap),
            };
            renderHost = new IndependentWpfBackgroundHost<BackgroundMessage>(
                new WindowInteropHelper(owner).Handle,
                initial,
                (context, message) => new AppleMusicWorkerRenderer(
                    context,
                    message,
                    deviceLatencyProvider,
                    artworkLoader),
                message => message.Dispose(),
                () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (disposed) return;
                    isReady = true;
                    Ready?.Invoke(this, EventArgs.Empty);
                })),
                ex => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!disposed)
                    {
                        Faulted?.Invoke(this, new BackgroundFaultedEventArgs(ex));
                    }
                })),
                threadName: "Lyricify Apple Music inspired background renderer");
            renderHost.Start();
        }

        private void Post(BackgroundMessage message)
        {
            if (!disposed) renderHost?.Post(message);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            isReady = false;
            artworkBitmap?.Dispose();
            artworkBitmap = null;
            renderHost?.Dispose();
            renderHost = null;
        }

        private enum MessageKind { Initialize, Artwork, Layout, Playback }

        private sealed class BackgroundMessage : IDisposable
        {
            public MessageKind Kind { get; set; }
            public AppleMusicInspiredBackgroundSettings? Settings { get; set; }
            public bool LightTheme { get; set; }
            public bool IsVertical { get; set; }
            public bool IsPlaying { get; set; }
            public string? ArtworkUrl { get; set; }
            public string TrackId { get; set; } = string.Empty;
            public Bitmap? ArtworkBitmap { get; set; }

            public static BackgroundMessage ForArtwork(string url, string id) =>
                new() { Kind = MessageKind.Artwork, ArtworkUrl = url, TrackId = id };

            public static BackgroundMessage ForArtwork(Bitmap bitmap, string id) =>
                new() { Kind = MessageKind.Artwork, ArtworkBitmap = bitmap, TrackId = id };

            public void Dispose() => ArtworkBitmap?.Dispose();
        }

        private sealed class AppleMusicWorkerRenderer :
            IIndependentWpfBackgroundRenderer<BackgroundMessage>
        {
            private readonly AppleMusicInspiredBackground renderer;
            private bool isPlaying;

            public AppleMusicWorkerRenderer(
                IndependentWpfBackgroundContext context,
                BackgroundMessage initial,
                Func<int>? latencyProvider,
                Func<string, Task<Bitmap>>? artworkLoader)
            {
                isPlaying = initial.IsPlaying;
                renderer = new AppleMusicInspiredBackground(
                    initial.Settings,
                    initial.LightTheme,
                    () => isPlaying,
                    context.WindowHandle,
                    context.FirstFramePresented,
                    latencyProvider,
                    artworkLoader);
                renderer.SetVerticalLayout(initial.IsVertical, false);
                renderer.SetIsBehindLyrics(true);
                renderer.SetPresentationVisible(true);
                ApplyArtwork(initial);
            }

            public UIElement Content => renderer;

            public void Apply(BackgroundMessage message)
            {
                switch (message.Kind)
                {
                    case MessageKind.Artwork: ApplyArtwork(message); break;
                    case MessageKind.Layout: renderer.SetVerticalLayout(message.IsVertical); break;
                    case MessageKind.Playback: isPlaying = message.IsPlaying; break;
                }
            }

            private void ApplyArtwork(BackgroundMessage message)
            {
                if (message.ArtworkBitmap != null)
                {
                    var copy = new Bitmap(message.ArtworkBitmap);
                    _ = ApplyBitmapAsync(copy, message.TrackId);
                }
                else if (!string.IsNullOrWhiteSpace(message.ArtworkUrl))
                {
                    renderer.SetArtwork(message.ArtworkUrl, message.TrackId);
                }
            }

            private async Task ApplyBitmapAsync(Bitmap bitmap, string id)
            {
                using (bitmap)
                {
                    await renderer.SetArtworkAsync(bitmap, id);
                }
            }

            public void Dispose()
            {
                renderer.SetPresentationVisible(false);
            }
        }
    }
}
