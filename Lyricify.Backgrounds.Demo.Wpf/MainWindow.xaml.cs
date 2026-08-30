using Lyricify.Backgrounds;
using Lyricify.Backgrounds.AppleMusicInspired.Ios.Wpf;
using Lyricify.Backgrounds.AppleMusicInspired.IosClassic.Wpf;
using Lyricify.Backgrounds.Demo.Shared;
using Lyricify.Backgrounds.Hosting.Wpf;
using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Lyricify.Backgrounds.Demo.Wpf;

public partial class MainWindow : Window
{
    private readonly DemoBackgroundViewModel viewModel = new();
    private readonly DispatcherTimer rebuildTimer;
    private WpfBackgroundInstance? preview;
    private byte[]? artwork;
    private string artworkId = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        rebuildTimer.Tick += (_, _) =>
        {
            rebuildTimer.Stop();
            RebuildPreview();
        };
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        PreviewHost.SizeChanged += (_, _) => PreviewClip.Rect = new Rect(PreviewHost.RenderSize);
        Loaded += async (_, _) =>
        {
            RebuildPreview();
            await LoadStartupArtworkAsync();
        };
        Closed += (_, _) => DisposePreview();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DemoBackgroundViewModel.ArtworkUrl) or nameof(DemoBackgroundViewModel.Status))
            return;

        if (preview == null)
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
        var catalog = new WpfBackgroundCatalog(
        [
            new AppleMusicIosWpfBackgroundFactory(
                lightTheme: viewModel.IsLightTheme,
                presetSlot: viewModel.SelectedPresetIndex - 1),
            new AppleMusicIosClassicWpfBackgroundFactory(
                lightTheme: viewModel.IsLightTheme,
                presetSlot: viewModel.SelectedPresetIndex - 1),
        ]);
        preview = catalog.Create(viewModel.SelectedBackgroundId, viewModel.Settings);
        preview.Session.FirstFramePresented += Preview_FirstFramePresented;
        preview.Session.Faulted += Preview_Faulted;
        PreviewHost.Children.Insert(0, preview.View);
        ApplyState();
        _ = ApplyArtworkAsync();
    }

    private void ApplyState() => preview?.Session.UpdateState(new BackgroundState
    {
        IsPlaying = viewModel.IsPlaying,
        IsVertical = viewModel.IsVertical,
        IsLightTheme = viewModel.IsLightTheme,
        IsBehindLyrics = viewModel.IsBehindLyrics,
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
        viewModel.Status = "The shared HLSL renderer is active.";

    private void Preview_Faulted(object? sender, BackgroundFaultedEventArgs e) =>
        viewModel.Status = e.Exception.Message;

    private async void LoadUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            artwork = await DemoArtworkLoader.LoadFromUriAsync(new Uri(viewModel.ArtworkUrl));
            artworkId = viewModel.ArtworkUrl;
            await ApplyArtworkAsync();
            viewModel.Status = "Artwork loaded.";
        }
        catch (Exception ex) { viewModel.Status = ex.Message; }
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            artwork = await DemoArtworkLoader.LoadFromFileAsync(dialog.FileName);
            artworkId = dialog.FileName;
            await ApplyArtworkAsync();
            viewModel.Status = "Artwork loaded.";
        }
        catch (Exception ex) { viewModel.Status = ex.Message; }
    }

    private async Task ApplyArtworkAsync()
    {
        WpfBackgroundInstance? target = preview;
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
            viewModel.Status = "Artwork loaded.";
        }
        catch (Exception exception)
        {
            viewModel.Status = exception.Message;
        }
    }

    private static string? GetCommandLineValue(string prefix) =>
        Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private void Reset_Click(object sender, RoutedEventArgs e) => viewModel.Reset();
}
