using Lyricify.Backgrounds.Hosting.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lyricify.Backgrounds.Hosting.Wpf;

public sealed class IndependentWpfBackgroundContext
{
    internal IndependentWpfBackgroundContext(IntPtr windowHandle, Action firstFramePresented)
    {
        WindowHandle = windowHandle;
        FirstFramePresented = firstFramePresented;
    }

    public IntPtr WindowHandle { get; }

    public Action FirstFramePresented { get; }
}

public interface IIndependentWpfBackgroundRenderer<in TMessage> : IDisposable
{
    UIElement Content { get; }

    void Apply(TMessage message);
}

public sealed class IndependentWpfBackgroundHost<TMessage> : IDisposable
    where TMessage : class
{
    private readonly Thread thread;
    private readonly IntPtr ownerHandle;
    private readonly TMessage initialMessage;
    private readonly Func<IndependentWpfBackgroundContext, TMessage,
        IIndependentWpfBackgroundRenderer<TMessage>> rendererFactory;
    private readonly Action<TMessage>? releaseMessage;
    private readonly Action? ready;
    private readonly Action<Exception>? faulted;
    private readonly TimeSpan placementInterval;
    private readonly object gate = new();
    private readonly List<TMessage> pendingMessages = new();
    private Dispatcher? dispatcher;
    private WorkerWindow? window;
    private bool started;
    private bool disposed;

    public IndependentWpfBackgroundHost(
        IntPtr ownerHandle,
        TMessage initialMessage,
        Func<IndependentWpfBackgroundContext, TMessage,
            IIndependentWpfBackgroundRenderer<TMessage>> rendererFactory,
        Action<TMessage>? releaseMessage = null,
        Action? ready = null,
        Action<Exception>? faulted = null,
        TimeSpan? placementInterval = null,
        string? threadName = null)
    {
        if (ownerHandle == IntPtr.Zero)
        {
            throw new ArgumentException("The owner window handle is required.", nameof(ownerHandle));
        }

        this.ownerHandle = ownerHandle;
        this.initialMessage = initialMessage ?? throw new ArgumentNullException(nameof(initialMessage));
        this.rendererFactory = rendererFactory ?? throw new ArgumentNullException(nameof(rendererFactory));
        this.releaseMessage = releaseMessage;
        this.ready = ready;
        this.faulted = faulted;
        this.placementInterval = placementInterval ?? TimeSpan.FromMilliseconds(50);
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = threadName ?? "Lyricify independent WPF background renderer",
        };
        thread.SetApartmentState(ApartmentState.STA);
    }

    public void Start()
    {
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(GetType().FullName);
            if (started) throw new InvalidOperationException("The background host has already started.");
            started = true;
        }
        thread.Start();
    }

    public void Post(TMessage message)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        Dispatcher? target;
        lock (gate)
        {
            if (disposed)
            {
                Release(message);
                return;
            }
            target = dispatcher;
            if (target == null || window == null)
            {
                pendingMessages.Add(message);
                return;
            }
        }

        if (target.HasShutdownStarted)
        {
            Release(message);
            return;
        }

        try
        {
            _ = target.BeginInvoke(new Action(() =>
            {
                try { window?.Apply(message); }
                finally { Release(message); }
            }));
        }
        catch (InvalidOperationException)
        {
            Release(message);
        }
    }

    private void Run()
    {
        Dispatcher currentDispatcher = Dispatcher.CurrentDispatcher;
        lock (gate)
        {
            if (disposed)
            {
                Release(initialMessage);
                return;
            }
            dispatcher = currentDispatcher;
        }

        bool initialReleased = false;
        try
        {
            window = new WorkerWindow(
                ownerHandle,
                initialMessage,
                rendererFactory,
                ready,
                placementInterval);
            Release(initialMessage);
            initialReleased = true;
            window.Show();

            TMessage[] messages;
            lock (gate)
            {
                messages = pendingMessages.ToArray();
                pendingMessages.Clear();
            }
            for (int index = 0; index < messages.Length; index++)
            {
                try
                {
                    window.Apply(messages[index]);
                }
                catch
                {
                    for (int remaining = index + 1; remaining < messages.Length; remaining++)
                    {
                        Release(messages[remaining]);
                    }
                    throw;
                }
                finally
                {
                    Release(messages[index]);
                }
            }
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            window?.Dispose();
            if (!disposed) faulted?.Invoke(ex);
        }
        finally
        {
            if (!initialReleased) Release(initialMessage);
            window = null;
            dispatcher = null;
        }
    }

    private void Release(TMessage message) => releaseMessage?.Invoke(message);

    public void Dispose()
    {
        bool releaseInitial;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            releaseInitial = !started;
            foreach (TMessage message in pendingMessages) Release(message);
            pendingMessages.Clear();
        }

        if (releaseInitial) Release(initialMessage);

        Dispatcher? target = dispatcher;
        if (target == null || target.HasShutdownStarted) return;
        _ = target.BeginInvoke(new Action(() =>
        {
            window?.Dispose();
            target.BeginInvokeShutdown(DispatcherPriority.Background);
        }));
    }

    private sealed class WorkerWindow : IDisposable
    {
        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExNoRedirectionBitmap = 0x00200000;
        private readonly HwndSource source;
        private readonly Grid root;
        private readonly DispatcherTimer placementTimer;
        private readonly OwnedBackgroundWindowTracker tracker;
        private readonly IIndependentWpfBackgroundRenderer<TMessage> renderer;
        private bool readyRaised;
        private bool disposed;

        public WorkerWindow(
            IntPtr ownerHandle,
            TMessage initialMessage,
            Func<IndependentWpfBackgroundContext, TMessage,
                IIndependentWpfBackgroundRenderer<TMessage>> rendererFactory,
            Action? ready,
            TimeSpan placementInterval)
        {
            var parameters = new HwndSourceParameters(string.Empty)
            {
                WindowStyle = WsPopup,
                ExtendedWindowStyle = WsExToolWindow | WsExNoActivate | WsExNoRedirectionBitmap,
                PositionX = -32000,
                PositionY = -32000,
                Width = 1,
                Height = 1,
                UsesPerPixelOpacity = false,
            };
            source = new HwndSource(parameters);
            root = new Grid { Background = Brushes.Black };
            source.RootVisual = root;
            tracker = new OwnedBackgroundWindowTracker(ownerHandle, source.Handle);
            var context = new IndependentWpfBackgroundContext(source.Handle, () =>
            {
                if (readyRaised) return;
                readyRaised = true;
                ready?.Invoke();
            });
            IIndependentWpfBackgroundRenderer<TMessage>? createdRenderer = null;
            try
            {
                createdRenderer = rendererFactory(context, initialMessage)
                    ?? throw new InvalidOperationException("The renderer factory returned null.");
                root.Children.Add(createdRenderer.Content);
                renderer = createdRenderer;
                placementTimer = new DispatcherTimer { Interval = placementInterval };
                placementTimer.Tick += (_, _) => tracker.Sync();
            }
            catch
            {
                createdRenderer?.Dispose();
                root.Children.Clear();
                tracker.Hide();
                source.RootVisual = null;
                source.Dispose();
                throw;
            }
        }

        public void Show()
        {
            placementTimer.Start();
            tracker.Sync();
        }

        public void Apply(TMessage message) => renderer.Apply(message);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            placementTimer.Stop();
            tracker.Hide();
            try
            {
                renderer.Dispose();
            }
            finally
            {
                root.Children.Clear();
                source.RootVisual = null;
                source.Dispose();
            }
        }
    }
}
