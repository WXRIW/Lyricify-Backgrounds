using Lyricify.Backgrounds;
using Lyricify.Backgrounds.AppleMusicInspired.Ios.WinUI;
using Lyricify.Backgrounds.AppleMusicInspired.IosClassic.WinUI;
using Lyricify.Backgrounds.Demo.Shared;
using Lyricify.Backgrounds.Hosting.WinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.ComponentModel;
using Windows.Graphics;

namespace Lyricify.Backgrounds.Demo.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer rebuildTimer;
    private WinUIBackgroundInstance? preview;
    private byte[]? artwork;
    private string artworkId = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Lyricify Backgrounds Demo · WinUI";
        rebuildTimer = DispatcherQueue.CreateTimer();
        rebuildTimer.Interval = TimeSpan.FromMilliseconds(180);
        rebuildTimer.Tick += (_, _) =>
        {
            rebuildTimer.Stop();
            RebuildPreview();
        };
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += (_, _) => DisposePreview();
        RebuildPreview();
        _ = LoadStartupArtworkAsync();
    }

    public DemoBackgroundViewModel ViewModel { get; } = new();

    public void CenterOnScreen()
    {
        DisplayArea displayArea = DisplayArea.GetFromWindowId(
            AppWindow.Id,
            DisplayAreaFallback.Primary);
        RectInt32 workArea = displayArea.WorkArea;
        SizeInt32 size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - size.Width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - size.Height) / 2)));
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DemoBackgroundViewModel.Status) or nameof(DemoBackgroundViewModel.ArtworkUrl))
            return;

        if (e.PropertyName is nameof(DemoBackgroundViewModel.IsPlaying)
            or nameof(DemoBackgroundViewModel.IsVertical)
            or nameof(DemoBackgroundViewModel.IsLightTheme)
            or nameof(DemoBackgroundViewModel.IsBehindLyrics))
        {
            ApplyState();
            return;
        }

        rebuildTimer.Stop();
        rebuildTimer.Start();
    }

    private void RebuildPreview()
    {
        DisposePreview();
        var catalog = new WinUIBackgroundCatalog(
        [
            new AppleMusicIosWinUIBackgroundFactory(ViewModel.SelectedPresetIndex - 1),
            new AppleMusicIosClassicWinUIBackgroundFactory(ViewModel.SelectedPresetIndex - 1),
        ]);
        preview = catalog.Create(ViewModel.SelectedBackgroundId, ViewModel.Settings);
        preview.Session.FirstFramePresented += Preview_FirstFramePresented;
        preview.Session.Faulted += Preview_Faulted;
        PreviewHost.Children.Add(preview.View);
        ApplyState();
        _ = ApplyArtworkAsync();
    }

    private void ApplyState() => preview?.Session.UpdateState(new BackgroundState
    {
        IsPlaying = ViewModel.IsPlaying,
        IsVertical = ViewModel.IsVertical,
        IsLightTheme = ViewModel.IsLightTheme,
        IsBehindLyrics = ViewModel.IsBehindLyrics,
        IsVisible = true,
    });

    private void DisposePreview()
    {
        rebuildTimer.Stop();
        if (preview == null) return;
        preview.Session.FirstFramePresented -= Preview_FirstFramePresented;
        preview.Session.Faulted -= Preview_Faulted;
        PreviewHost.Children.Remove(preview.View);
        preview.Dispose();
        preview = null;
    }

    private void Preview_FirstFramePresented(object? sender, EventArgs e) =>
        SetStatus("The shared HLSL renderer is active.");

    private void Preview_Faulted(object? sender, BackgroundFaultedEventArgs e) =>
        SetStatus(e.Exception.Message);

    private async void LoadUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            artwork = await DemoArtworkLoader.LoadFromUriAsync(new Uri(ArtworkUrlBox.Text));
            artworkId = ArtworkUrlBox.Text;
            SetStatus("Artwork bytes loaded.");
            await ApplyArtworkAsync();
            SetStatus("Artwork loaded.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new global::Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            global::Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
            if (file == null) return;
            artwork = await DemoArtworkLoader.LoadFromFileAsync(file.Path);
            artworkId = file.Path;
            SetStatus("Artwork bytes loaded.");
            await ApplyArtworkAsync();
            SetStatus("Artwork loaded.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private async Task ApplyArtworkAsync()
    {
        WinUIBackgroundInstance? target = preview;
        if (artwork == null || target == null) return;
        await target.Session.SetArtworkAsync(new BackgroundArtwork(artworkId, artwork));
    }

    private async Task LoadStartupArtworkAsync()
    {
        string? path = GetCommandLineValue("--artwork=");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            artwork = await DemoArtworkLoader.LoadFromFileAsync(path);
            artworkId = path;
            await ApplyArtworkAsync();
            SetStatus("Artwork loaded.");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    private static string? GetCommandLineValue(string prefix) =>
        Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private void Reset_Click(object sender, RoutedEventArgs e) => ViewModel.Reset();

    private void SetStatus(string value)
    {
        if (DispatcherQueue.HasThreadAccess)
            ViewModel.Status = value;
        else
            DispatcherQueue.TryEnqueue(() => ViewModel.Status = value);
    }
}
